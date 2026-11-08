using System.Text;
using FinFlow.Infrastructure.Messaging.RabbitMq;
using FinFlow.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace FinFlow.Infrastructure.Messaging.Outbox;

/// <summary>
/// Lê periodicamente o outbox e publica as mensagens pendentes no RabbitMQ.
/// Só marca como publicada depois da confirmação do broker (publisher confirms),
/// então nenhuma mensagem é perdida. Se o processo cair entre a confirmação e a
/// gravação no banco, a mensagem é publicada de novo — o Worker é idempotente.
/// </summary>
public sealed class OutboxPublisherService(
    IServiceScopeFactory scopeFactory,
    RabbitMqConnectionProvider connectionProvider,
    ILogger<OutboxPublisherService> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await PublishPendingAsync(stoppingToken);
                if (published < BatchSize)
                    await Task.Delay(IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao publicar mensagens do outbox. Nova tentativa em {Delay}s.",
                    ErrorDelay.TotalSeconds);
                await ResetChannelAsync();
                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }

    private async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinFlowDbContext>();

        var messages = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
            return 0;

        var channel = await GetChannelAsync(cancellationToken);
        var published = 0;

        foreach (var message in messages)
        {
            try
            {
                var properties = new BasicProperties
                {
                    MessageId = message.Id.ToString(),
                    Type = message.Type,
                    ContentType = "application/json",
                    DeliveryMode = DeliveryModes.Persistent,
                    Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds())
                };

                // Com confirmação habilitada, o await só retorna após o ack do broker.
                await channel.BasicPublishAsync(
                    RabbitMqTopology.Exchange,
                    routingKey: message.Type,
                    mandatory: true,
                    basicProperties: properties,
                    body: Encoding.UTF8.GetBytes(message.Payload),
                    cancellationToken: cancellationToken);

                message.ProcessedAt = DateTimeOffset.UtcNow;
                message.LastError = null;
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                logger.LogWarning(ex, "Falha ao publicar mensagem {MessageId} ({Type}).", message.Id, message.Type);
                await ResetChannelAsync();
                break; // mantém a ordem: tenta de novo no próximo ciclo
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (published > 0)
            logger.LogInformation("{Count} mensagem(ns) publicada(s) no RabbitMQ.", published);

        return published;
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await RabbitMqTopology.DeclareAsync(_channel, cancellationToken);
        return _channel;
    }

    private async Task ResetChannelAsync()
    {
        if (_channel is null)
            return;

        try { await _channel.DisposeAsync(); }
        catch { /* canal já pode estar fechado */ }

        _channel = null;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await ResetChannelAsync();
    }
}
