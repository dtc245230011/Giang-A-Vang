using System.Text.Json;
using SupportTicket.Contracts;
using SupportTicket.Infrastructure.Entities;

namespace SupportTicket.Infrastructure.Messaging;

public static class OutboxFactory
{
    public static OutboxMessage Create<T>(T payload, Guid messageId, string eventType, string routingKey, DateTime occurredAt)
        where T : notnull
        => new()
        {
            Id = messageId,
            EventType = eventType,
            RoutingKey = routingKey,
            Payload = JsonSerializer.Serialize(payload, MessagingJson.Options),
            OccurredAt = occurredAt,
            NextAttemptAt = occurredAt
        };
}
