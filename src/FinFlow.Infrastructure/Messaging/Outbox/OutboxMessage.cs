namespace FinFlow.Infrastructure.Messaging.Outbox;

/// <summary>Mensagem aguardando publicação no RabbitMQ.</summary>
public sealed class OutboxMessage
{
    /// <summary>Também é usado como MessageId no RabbitMQ.</summary>
    public Guid Id { get; set; }

    /// <summary>Tipo/routing key (ex.: transaction.requested).</summary>
    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Preenchido quando o RabbitMQ confirmou o recebimento.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}
