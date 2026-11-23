namespace SupportTicket.Contracts;

/// <summary>Tên sự kiện được dùng trong cột Outbox và thuộc tính Type của message.</summary>
public static class EventTypes
{
    public const string TicketCreated = "TicketCreated";
    public const string TicketAssigned = "TicketAssigned";
    public const string TicketStatusChanged = "TicketStatusChanged";
}

/// <summary>Routing key của RabbitMQ topic exchange.</summary>
public static class RoutingKeys
{
    public const string TicketCreated = "ticket.created";
    public const string TicketAssigned = "ticket.assigned";
    public const string TicketStatusChanged = "ticket.status-changed";
}

/// <summary>Tên exchange, queue và quy ước retry/DLQ dùng chung cho API và Worker.</summary>
public static class MessagingTopology
{
    public const string Exchange = "support-tickets";
    public const string DeadLetterExchange = "support-tickets-dlx";

    public const string AssignmentQueue = "assignment-worker";
    public const string AssignmentRetryQueue = "assignment-worker.retry";
    public const string AssignmentDeadLetterQueue = "assignment-worker.dlq";

    public const string NotificationQueue = "notification-service";
    public const string NotificationRetryQueue = "notification-service.retry";
    public const string NotificationDeadLetterQueue = "notification-service.dlq";

    public const int MaxDeliveryAttempts = 3;
    public const int RetryDelayMilliseconds = 5_000;
}

/// <summary>Sự kiện phát khi ticket mới được tạo.</summary>
public sealed record TicketCreatedEvent(
    Guid MessageId,
    Guid TicketId,
    string TicketNumber,
    TicketCategory Category,
    TicketPriority Priority,
    Guid RequesterId,
    DateTime OccurredAt,
    int Version = 1);

/// <summary>Sự kiện phát khi ticket được giao nhóm/người xử lý.</summary>
public sealed record TicketAssignedEvent(
    Guid MessageId,
    Guid TicketId,
    string TicketNumber,
    string Title,
    Guid TeamId,
    string TeamCode,
    string TeamName,
    Guid? AssignedUserId,
    string? AssignedUserName,
    TicketStatus OldStatus,
    TicketStatus NewStatus,
    Guid? ChangedByUserId,
    DateTime OccurredAt,
    int Version = 1);

/// <summary>Sự kiện phát khi trạng thái ticket thay đổi.</summary>
public sealed record TicketStatusChangedEvent(
    Guid MessageId,
    Guid TicketId,
    string TicketNumber,
    string Title,
    TicketStatus OldStatus,
    TicketStatus NewStatus,
    Guid? ChangedByUserId,
    Guid? TeamId,
    string? TeamName,
    Guid? AssignedUserId,
    string? AssignedUserName,
    DateTime OccurredAt,
    int Version = 1);
