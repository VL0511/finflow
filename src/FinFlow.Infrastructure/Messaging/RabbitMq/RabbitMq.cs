using FinFlow.Application.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace FinFlow.Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ClientName { get; set; } = "finflow";
}

/// <summary>Exchanges, filas e bindings usados pelo FinFlow.</summary>
public static class RabbitMqTopology
{
    public const string Exchange = "finflow.events";
    public const string DeadLetterExchange = "finflow.events.dlx";

    public const string TransactionRequestedQueue = "finflow.transactions.requested";
    public const string TransactionRequestedDeadLetterQueue = "finflow.transactions.requested.dlq";

    /// <summary>Declaração idempotente: pode ser chamada pela API e pelo Worker.</summary>
    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);

        // Fila de mensagens que falharam repetidamente (para análise/reprocessamento).
        await channel.QueueDeclareAsync(TransactionRequestedDeadLetterQueue, durable: true, exclusive: false,
            autoDelete: false, arguments: null, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(TransactionRequestedDeadLetterQueue, DeadLetterExchange,
            TransactionRequested.MessageType, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(TransactionRequestedQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchange },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(TransactionRequestedQueue, Exchange,
            TransactionRequested.MessageType, cancellationToken: cancellationToken);
    }
}

/// <summary>Mantém uma única conexão por processo (com reconexão automática).</summary>
public sealed class RabbitMqConnectionProvider(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConnectionProvider> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
            return _connection;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not null)
                return _connection;

            var o = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = o.HostName,
                Port = o.Port,
                UserName = o.UserName,
                Password = o.Password,
                VirtualHost = o.VirtualHost,
                ClientProvidedName = o.ClientName,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            logger.LogInformation("Conectado ao RabbitMQ em {Host}:{Port}.", o.HostName, o.Port);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        _lock.Dispose();
    }
}
