using RabbitMQ.Client;
using SupportTicket.Contracts;

namespace SupportTicket.Infrastructure.Messaging;

public static class Topology
{
    /// <summary>Khai báo exchange, queue, retry queue và DLQ. Chạy lại nhiều lần vẫn an toàn.</summary>
    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            Naming.Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            arguments: null, passive: false, noWait: false, cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            Naming.DeadLetterExchange, ExchangeType.Topic, durable: true, autoDelete: false,
            arguments: null, passive: false, noWait: false, cancellationToken: cancellationToken);

        await DeclareQueueWithDeadLetterAsync(
            channel, Naming.AssignmentQueue, cancellationToken, RoutingKeys.TicketCreated);

        await DeclareQueueWithDeadLetterAsync(
            channel, Naming.NotificationQueue, cancellationToken,
            RoutingKeys.TicketAssigned, RoutingKeys.TicketStatusChanged);

        await DeclareRetryQueueAsync(channel, Naming.AssignmentQueue, cancellationToken);
        await DeclareRetryQueueAsync(channel, Naming.NotificationQueue, cancellationToken);

        await DeclareDeadLetterQueueAsync(channel, Naming.AssignmentDeadLetterQueue, cancellationToken);
        await DeclareDeadLetterQueueAsync(channel, Naming.NotificationDeadLetterQueue, cancellationToken);
    }

    private static async Task DeclareQueueWithDeadLetterAsync(
        IChannel channel, string queue, CancellationToken cancellationToken, params string[] routingKeys)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = Naming.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = $"{queue}.dlq"
        };

        await channel.QueueDeclareAsync(
            queue, durable: true, exclusive: false, autoDelete: false,
            arguments: arguments, passive: false, noWait: false, cancellationToken: cancellationToken);

        foreach (var routingKey in routingKeys)
        {
            await channel.QueueBindAsync(
                queue, Naming.Exchange, routingKey, arguments: null,
                noWait: false, cancellationToken: cancellationToken);
        }
    }

    private static async Task DeclareRetryQueueAsync(IChannel channel, string sourceQueue, CancellationToken cancellationToken)
    {
        var retryQueue = $"{sourceQueue}.retry";
        var arguments = new Dictionary<string, object?>
        {
            ["x-message-ttl"] = MessagingTopology.RetryDelayMilliseconds,
            ["x-dead-letter-exchange"] = string.Empty,
            ["x-dead-letter-routing-key"] = sourceQueue
        };

        await channel.QueueDeclareAsync(
            retryQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: arguments, passive: false, noWait: false, cancellationToken: cancellationToken);
    }

    private static async Task DeclareDeadLetterQueueAsync(IChannel channel, string deadLetterQueue, CancellationToken cancellationToken)
    {
        await channel.QueueDeclareAsync(
            deadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: null, passive: false, noWait: false, cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            deadLetterQueue, Naming.DeadLetterExchange, $"{deadLetterQueue}",
            arguments: null, noWait: false, cancellationToken: cancellationToken);
    }

    private static class Naming
    {
        public const string Exchange = MessagingTopology.Exchange;
        public const string DeadLetterExchange = MessagingTopology.DeadLetterExchange;
        public const string AssignmentQueue = MessagingTopology.AssignmentQueue;
        public const string AssignmentDeadLetterQueue = MessagingTopology.AssignmentDeadLetterQueue;
        public const string NotificationQueue = MessagingTopology.NotificationQueue;
        public const string NotificationDeadLetterQueue = MessagingTopology.NotificationDeadLetterQueue;
    }
}
