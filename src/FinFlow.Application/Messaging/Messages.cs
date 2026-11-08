namespace FinFlow.Application.Messaging;

/// <summary>Mensagem trafegada pelo RabbitMQ.</summary>
public interface IIntegrationMessage
{
    /// <summary>Tipo da mensagem, usado também como routing key.</summary>
    static abstract string MessageType { get; }
}

/// <summary>
/// Outbox transacional: a mensagem é gravada na mesma transação SQL da
/// alteração de negócio e publicada no RabbitMQ depois, em background.
/// </summary>
public interface IMessageOutbox
{
    void Enqueue<TMessage>(TMessage message) where TMessage : IIntegrationMessage;
}

/// <summary>Uma transferência foi solicitada e aguarda processamento pelo Worker.</summary>
public sealed record TransactionRequested(
    Guid TransactionId,
    string SourceAccount,
    string DestinationAccount,
    decimal Amount,
    string Currency,
    DateTimeOffset RequestedAt) : IIntegrationMessage
{
    public static string MessageType => "transaction.requested";
}
