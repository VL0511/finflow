using System.Text.Json;
using FinFlow.Worker.Processing;
using FinFlow.Application.Messaging;
using FinFlow.Application.Transactions;
using FinFlow.Infrastructure.Messaging.Outbox;
using FinFlow.Infrastructure.Messaging.RabbitMq;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FinFlow.Worker.Consumers;

/// <summary>
/// Consome TransactionRequested e executa a transferência.
/// - Ack manual: a mensagem só sai da fila depois do commit no SQL Server.
/// - Falha transitória (conflito, deadlock, timeout, banco indisponível): retry com backoff
///   exponencial + jitter; se persistir, a mensagem volta para a fila (nunca para a DLQ).
/// - Erro persistente ou mensagem inválida: vai para a DLQ. Transações que ficarem em
///   Processing/Pending são retomadas pela StuckTransactionRecoveryService.
/// - Até <see cref="ConsumerOptions.MaxConcurrency"/> mensagens em paralelo. A mesma transferência
///   entregue duas vezes ao mesmo tempo é serializada pelo handler (UPDLOCK), não por este limite.
/// - Resiliência: queda de conexão é tratada pela recuperação automática do cliente; canal fechado
///   por erro ou consumidor cancelado pelo broker (ex.: fila removida) reinicia o consumo.
/// - Desligamento: para de receber, espera as mensagens em andamento terminarem (até o
///   ShutdownTimeout do host) e só então fecha o canal — nada é confirmado pela metade.
/// </summary>
public sealed class TransactionRequestedConsumer(
    TransactionProcessor processor,
    RabbitMqConnectionProvider connectionProvider,
    IOptions<ConsumerOptions> options,
    ILogger<TransactionRequestedConsumer> logger) : BackgroundService
{
    private IChannel? _channel;
    private string? _consumerTag;
    private CancellationToken _stoppingToken;

    private int _inFlight;
    private volatile bool _stopping;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Mensagens em processamento neste momento (diagnóstico e testes).</summary>
    public int InFlight => Volatile.Read(ref _inFlight);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        var settings = options.Value;

        while (!stoppingToken.IsCancellationRequested)
        {
            var interrupted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await StartConsumingAsync(settings, interrupted, stoppingToken);

                var reason = await interrupted.Task.WaitAsync(stoppingToken);
                logger.LogWarning("Consumo da fila {Queue} interrompido: {Reason}. Reiniciando em {Delay}.",
                    RabbitMqTopology.TransactionRequestedQueue, reason, settings.ReconnectDelay);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // desligamento: o StopAsync fecha o canal depois de drenar as mensagens
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Não foi possível consumir a fila {Queue} ({Error}). Nova tentativa em {Delay}.",
                    RabbitMqTopology.TransactionRequestedQueue, ex.Message, settings.ReconnectDelay);
            }

            await CloseChannelAsync();

            try
            {
                await Task.Delay(settings.ReconnectDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task StartConsumingAsync(
        ConsumerOptions settings, TaskCompletionSource<string> interrupted, CancellationToken stoppingToken)
    {
        var connection = await connectionProvider.GetConnectionAsync(stoppingToken);

        // consumerDispatchConcurrency: quantos ReceivedAsync o cliente executa em paralelo neste canal
        // (o padrão é 1, ou seja, uma mensagem por vez independentemente do prefetch).
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: (ushort)settings.MaxConcurrency),
            stoppingToken);
        _channel = channel;

        channel.ChannelShutdownAsync += (_, args) =>
        {
            // Fechado por nós (desligamento/reinício) ou junto com a conexão (a recuperação automática
            // do cliente restaura canal e consumidor): nada a fazer. Fechado sozinho, com a conexão
            // de pé (erro de protocolo), não volta mais: reinicia o consumo.
            if (args.Initiator != ShutdownInitiator.Application && connection.IsOpen && !_stopping)
                interrupted.TrySetResult($"canal fechado ({args.ReplyCode} {args.ReplyText})");
            return Task.CompletedTask;
        };

        await RabbitMqTopology.DeclareAsync(channel, stoppingToken);

        var prefetch = (ushort)Math.Max(settings.PrefetchCount, settings.MaxConcurrency);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: prefetch, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += OnMessageAsync;
        consumer.UnregisteredAsync += (_, _) =>
        {
            // Cancelamento que não partiu de nós: fila removida, failover de nó etc.
            if (!_stopping)
                interrupted.TrySetResult("consumidor cancelado pelo broker");
            return Task.CompletedTask;
        };

        _consumerTag = await channel.BasicConsumeAsync(
            RabbitMqTopology.TransactionRequestedQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation(
            "Consumindo a fila {Queue} (até {MaxConcurrency} mensagens em paralelo, prefetch {Prefetch}).",
            RabbitMqTopology.TransactionRequestedQueue, settings.MaxConcurrency, prefetch);
    }

    private async Task OnMessageAsync(object sender, BasicDeliverEventArgs ea)
    {
        // Conta antes de olhar _stopping: o StopAsync nunca vê zero enquanto esta mensagem
        // pode estar começando a ser processada.
        Interlocked.Increment(ref _inFlight);
        try
        {
            var channel = ((AsyncEventingBasicConsumer)sender).Channel;
            if (_stopping)
            {
                // Já entregue (prefetch), mas o Worker está desligando: devolve sem começar.
                await SettleAsync(channel, ea, Settlement.Requeue, ea.BasicProperties.MessageId);
                return;
            }

            await HandleAsync(channel, ea);
        }
        finally
        {
            if (Interlocked.Decrement(ref _inFlight) == 0 && _stopping)
                _drained.TrySetResult();
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs ea)
    {
        var messageId = ea.BasicProperties.MessageId ?? "(sem id)";
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["MessageId"] = messageId });

        TransactionRequested? message;
        try
        {
            message = JsonSerializer.Deserialize<TransactionRequested>(ea.Body.Span, MessagingJson.Options);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Mensagem {MessageId} inválida; enviada para a DLQ.", messageId);
            await SettleAsync(channel, ea, Settlement.DeadLetter, messageId);
            return;
        }

        if (message is null || message.TransactionId == Guid.Empty)
        {
            logger.LogError("Mensagem {MessageId} sem TransactionId; enviada para a DLQ.", messageId);
            await SettleAsync(channel, ea, Settlement.DeadLetter, messageId);
            return;
        }

        // Só o TransactionId é usado: valores, contas e moeda são relidos do banco pelo handler,
        // então uma mensagem adulterada ou desatualizada não altera o que é movimentado.
        using var transactionScope = logger.BeginScope(new Dictionary<string, object> { ["TransactionId"] = message.TransactionId });

        if (ea.Redelivered)
            logger.LogInformation("Mensagem {MessageId} é uma reentrega.", messageId);

        RetryResult<ProcessTransactionOutcome> result;
        try
        {
            result = await processor.ProcessAsync(message.TransactionId, $"mensagem {messageId}", _stoppingToken);
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            // Tempo de desligamento esgotado no meio do backoff: nada foi comitado pela tentativa
            // interrompida. Devolve a mensagem para outra instância (ou para este Worker ao voltar).
            await SettleAsync(channel, ea, Settlement.Requeue, messageId);
            return;
        }

        switch (result.Kind)
        {
            case RetryResultKind.Succeeded:
                logger.LogInformation(
                    "Transação {TransactionId} (mensagem {MessageId}): {Outcome} (tentativas: {Attempts}).",
                    message.TransactionId, messageId, result.Value, result.Attempts);
                await SettleAsync(channel, ea, Settlement.Ack, messageId);
                break;

            case RetryResultKind.TransientFailure:
                // Conflito/deadlock/indisponibilidade são temporários: a mensagem NÃO vai para a DLQ.
                // Volta para a fila e será processada de novo (o handler é idempotente).
                logger.LogWarning(result.Error,
                    "Transação {TransactionId} (mensagem {MessageId}): falha transitória persistiu por {Attempts} tentativas; mensagem devolvida à fila.",
                    message.TransactionId, messageId, result.Attempts);
                await SettleAsync(channel, ea, Settlement.Requeue, messageId);
                break;

            default:
                // Erro não transitório: DLQ para análise (sem loop). Se a transação ficou em
                // Processing/Pending, a StuckTransactionRecoveryService a retoma depois, sem duplicar.
                logger.LogError(result.Error,
                    "Transação {TransactionId} (mensagem {MessageId}) falhou após {Attempts} tentativas; mensagem enviada para a DLQ.",
                    message.TransactionId, messageId, result.Attempts);
                await SettleAsync(channel, ea, Settlement.DeadLetter, messageId);
                break;
        }
    }

    private enum Settlement { Ack, Requeue, DeadLetter }

    /// <summary>
    /// ACK/NACK sem derrubar o consumidor se o canal tiver caído: a mensagem sem confirmação
    /// é reentregue pelo RabbitMQ, e o reprocessamento é idempotente (status da transação).
    /// </summary>
    private async Task SettleAsync(IChannel channel, BasicDeliverEventArgs ea, Settlement settlement, string? messageId)
    {
        try
        {
            switch (settlement)
            {
                case Settlement.Ack:
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                    break;
                case Settlement.Requeue:
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
                    break;
                default:
                    // requeue: false + x-dead-letter-exchange da fila = DLQ.
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Não foi possível registrar {Settlement} da mensagem {MessageId} (canal fechado); o RabbitMQ a reentregará e o reprocessamento é idempotente.",
                settlement, messageId ?? "(sem id)");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // 1) Para de receber; 2) espera as mensagens em andamento (até o ShutdownTimeout do host);
        // 3) cancela o ExecuteAsync; 4) fecha o canal. A conexão é fechada pelo container de DI.
        _stopping = true;
        if (InFlight == 0)
            _drained.TrySetResult();

        await CancelConsumerAsync();

        try
        {
            await _drained.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Tempo de desligamento esgotado com {InFlight} mensagem(ns) em processamento; sem ACK, o RabbitMQ as reentregará.",
                InFlight);
        }

        await base.StopAsync(cancellationToken);
        await CloseChannelAsync();
        logger.LogInformation("Consumo da fila {Queue} encerrado.", RabbitMqTopology.TransactionRequestedQueue);
    }

    private async Task CancelConsumerAsync()
    {
        if (_channel is not { IsOpen: true } channel || _consumerTag is null)
            return;

        try
        {
            await channel.BasicCancelAsync(_consumerTag, noWait: false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Não foi possível cancelar o consumidor {ConsumerTag}.", _consumerTag);
        }
    }

    private async Task CloseChannelAsync()
    {
        var channel = _channel;
        _channel = null;
        _consumerTag = null;
        if (channel is null)
            return;

        try
        {
            await channel.DisposeAsync();
        }
        catch (Exception ex)
        {
            // Canal já pode estar fechado (queda de conexão): sem ACK, as mensagens voltam à fila.
            logger.LogDebug(ex, "Canal do consumidor já estava fechado.");
        }
    }
}
