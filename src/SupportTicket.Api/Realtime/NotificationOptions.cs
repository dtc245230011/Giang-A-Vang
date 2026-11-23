namespace SupportTicket.Api.Realtime;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Bật consumer RabbitMQ gửi thông báo SignalR.</summary>
    public bool ConsumerEnabled { get; set; } = true;
}
