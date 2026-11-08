using System.Text.Json;
using FinFlow.Application.Messaging;
using FinFlow.Infrastructure.Persistence.Context;

namespace FinFlow.Infrastructure.Messaging.Outbox;

/// <summary>
/// Adiciona a mensagem ao DbContext. Ela é gravada no mesmo SaveChanges das
/// alterações de negócio, então ou ambas persistem, ou nenhuma.
/// </summary>
public sealed class EfMessageOutbox(FinFlowDbContext context) : IMessageOutbox
{
    public void Enqueue<TMessage>(TMessage message) where TMessage : IIntegrationMessage
    {
        context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = TMessage.MessageType,
            Payload = JsonSerializer.Serialize(message, MessagingJson.Options),
            OccurredAt = DateTimeOffset.UtcNow
        });
    }
}

public static class MessagingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
