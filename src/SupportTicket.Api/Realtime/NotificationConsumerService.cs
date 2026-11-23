using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SupportTicket.Contracts;
using SupportTicket.Infrastructure.Data;
using SupportTicket.Infrastructure.Entities;
using SupportTicket.Infrastructure.Messaging;

namespace SupportTicket.Api.Realtime;

/// <summary>
/// Nhận TicketAssigned / TicketStatusChanged từ RabbitMQ và gửi SignalR tới đúng người có quyền.
/// Nội dung gửi chỉ gồm thông tin tóm tắt, không gửi mô tả ticket.
/// </summary>
public sealed class NotificationConsumerService(
    RabbitMqConnection connection,
    RabbitMqPublisher publisher,
    IHubContext<NotificationHub> hub,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> rabbitOptions,
    IOptions<NotificationOptions> notificationOptions,
    ILogger<NotificationConsumerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!rabbitOptions.Value.Enabled || !notificationOptions.Value.ConsumerEnabled)
        {
            logger.LogInformation("Notification consumer bị tắt bởi cấu hình.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Notification consumer mất kết nối; thử lại sau 5 giây.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var rabbit = await connection.GetConnectionAsync(stoppingToken);
        await using var channel = await rabbit.CreateChannelAsync(cancellationToken: stoppingToken);
        await Topology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, 10, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            await HandleMessageAsync(channel, args, stoppingToken);
        };

        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, args) =>
        {
            logger.LogWarning("Notification channel đóng: {Reason}.", args.ReplyText);
            shutdown.TrySetResult();
            return Task.CompletedTask;
        };

        await channel.BasicConsumeAsync(
            MessagingTopology.NotificationQueue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        logger.LogInformation("Notification consumer đang lắng nghe queue {Queue}.", MessagingTopology.NotificationQueue);

        await Task.WhenAny(Task.Delay(Timeout.Infinite, stoppingToken), shutdown.Task);
    }

    private async Task HandleMessageAsync(IChannel channel, BasicDeliverEventArgs args, CancellationToken cancellationToken)
    {
        var messageId = Guid.TryParse(args.BasicProperties.MessageId, out var parsed) ? parsed : Guid.NewGuid();
        var eventType = args.BasicProperties.Type ?? string.Empty;
        var payload = Encoding.UTF8.GetString(args.Body.Span);

        try
        {
            await DispatchAsync(messageId, eventType, payload, cancellationToken);
            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Xử lý thông báo {MessageId} ({EventType}) thất bại.", messageId, eventType);
            await RetryOrDeadLetterAsync(channel, args, messageId, eventType, payload, exception, cancellationToken);
        }
    }

    private async Task DispatchAsync(
        Guid messageId, string eventType, string payload, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.InboxMessages.AnyAsync(x => x.MessageId == messageId, cancellationToken))
        {
            logger.LogInformation("Bỏ qua thông báo trùng {MessageId}.", messageId);
            return;
        }

        switch (eventType)
        {
            case EventTypes.TicketAssigned:
            {
                var message = JsonSerializer.Deserialize<TicketAssignedEvent>(payload, MessagingJson.Options)
                              ?? throw new InvalidOperationException("Payload TicketAssigned không hợp lệ.");

                var ticket = await LoadTicketAsync(db, message.TicketId, cancellationToken);
                if (ticket is null)
                {
                    logger.LogWarning("Không tìm thấy ticket {TicketId} cho thông báo phân công.", message.TicketId);
                    break;
                }

                var recipients = await ResolveRecipientsAsync(db, ticket, message.ChangedByUserId, cancellationToken);
                await SendAsync(recipients, EventTypes.TicketAssigned, new
                {
                    ticketId = ticket.Id,
                    ticketNumber = ticket.TicketNumber,
                    title = ticket.Title,
                    status = ticket.Status.ToString(),
                    category = ticket.Category.ToString(),
                    teamId = ticket.AssignedTeamId,
                    teamName = ticket.AssignedTeam?.Name,
                    assignedUserId = ticket.AssignedUserId,
                    assignedUserName = ticket.AssignedUser?.FullName,
                    eventType = EventTypes.TicketAssigned,
                    changedAt = message.OccurredAt,
                    changedByUserId = message.ChangedByUserId
                }, cancellationToken);
                break;
            }

            case EventTypes.TicketStatusChanged:
            {
                var message = JsonSerializer.Deserialize<TicketStatusChangedEvent>(payload, MessagingJson.Options)
                              ?? throw new InvalidOperationException("Payload TicketStatusChanged không hợp lệ.");

                var ticket = await LoadTicketAsync(db, message.TicketId, cancellationToken);
                if (ticket is null)
                {
                    logger.LogWarning("Không tìm thấy ticket {TicketId} cho thông báo trạng thái.", message.TicketId);
                    break;
                }

                var recipients = await ResolveRecipientsAsync(db, ticket, message.ChangedByUserId, cancellationToken);
                await SendAsync(recipients, EventTypes.TicketStatusChanged, new
                {
                    ticketId = ticket.Id,
                    ticketNumber = ticket.TicketNumber,
                    title = ticket.Title,
                    status = ticket.Status.ToString(),
                    oldStatus = message.OldStatus.ToString(),
                    newStatus = message.NewStatus.ToString(),
                    category = ticket.Category.ToString(),
                    teamId = ticket.AssignedTeamId,
                    teamName = ticket.AssignedTeam?.Name,
                    assignedUserId = ticket.AssignedUserId,
                    assignedUserName = ticket.AssignedUser?.FullName,
                    eventType = EventTypes.TicketStatusChanged,
                    changedAt = message.OccurredAt,
                    changedByUserId = message.ChangedByUserId
                }, cancellationToken);
                break;
            }

            default:
                throw new InvalidOperationException($"Loại sự kiện không được hỗ trợ: {eventType}.");
        }

        db.InboxMessages.Add(new InboxMessage
        {
            MessageId = messageId,
            EventType = eventType,
            ProcessedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Ticket?> LoadTicketAsync(AppDbContext db, Guid ticketId, CancellationToken cancellationToken)
        => await db.Tickets.AsNoTracking()
            .Include(x => x.AssignedTeam)
            .Include(x => x.AssignedUser)
            .FirstOrDefaultAsync(x => x.Id == ticketId, cancellationToken);

    /// <summary>Người tạo, người được gán và thành viên nhóm xử lý; loại trừ người vừa thay đổi.</summary>
    private static async Task<List<Guid>> ResolveRecipientsAsync(
        AppDbContext db, Ticket ticket, Guid? changedByUserId, CancellationToken cancellationToken)
    {
        var recipients = new HashSet<Guid> { ticket.RequesterId };

        if (ticket.AssignedUserId is { } assignedUserId)
        {
            recipients.Add(assignedUserId);
        }

        if (ticket.AssignedTeamId is { } teamId)
        {
            var members = await db.TeamMembers.AsNoTracking()
                .Where(x => x.TeamId == teamId && x.User.IsActive)
                .Select(x => x.UserId)
                .ToListAsync(cancellationToken);
            recipients.UnionWith(members);
        }

        if (changedByUserId is { } actor)
        {
            recipients.Remove(actor);
        }

        return recipients.ToList();
    }

    private async Task SendAsync<T>(
        IReadOnlyCollection<Guid> recipients, string eventName, T payload, CancellationToken cancellationToken)
    {
        if (recipients.Count == 0)
        {
            return;
        }

        var userIds = recipients.Select(x => x.ToString()).ToArray();
        await hub.Clients.Users(userIds).SendAsync(eventName, payload, cancellationToken);
        logger.LogInformation("Đã gửi {EventName} tới {Count} người dùng.", eventName, recipients.Count);
    }

    private async Task RetryOrDeadLetterAsync(
        IChannel channel, BasicDeliverEventArgs args, Guid messageId, string eventType, string payload,
        Exception exception, CancellationToken cancellationToken)
    {
        var retryCount = ReadRetryCount(args.BasicProperties.Headers);

        try
        {
            if (retryCount < MessagingTopology.MaxDeliveryAttempts)
            {
                await publisher.PublishRawAsync(
                    exchange: string.Empty,
                    routingKey: MessagingTopology.NotificationRetryQueue,
                    messageId, eventType, payload, retryCount + 1, cancellationToken);
                logger.LogWarning(
                    "Đưa thông báo {MessageId} vào retry queue (lần {Retry}/{Max}). Lỗi: {Message}",
                    messageId, retryCount + 1, MessagingTopology.MaxDeliveryAttempts, exception.Message);
            }
            else
            {
                await publisher.PublishRawAsync(
                    exchange: string.Empty,
                    routingKey: MessagingTopology.NotificationDeadLetterQueue,
                    messageId, eventType, payload, retryCount + 1, cancellationToken);
                logger.LogError(
                    "Đưa thông báo {MessageId} vào DLQ sau {Retry} lần thử. Lỗi: {Message}",
                    messageId, retryCount + 1, exception.Message);
            }

            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception publishException)
        {
            logger.LogError(publishException, "Không thể đưa thông báo {MessageId} sang retry/DLQ; trả message về broker.", messageId);
            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken);
        }
    }

    private static int ReadRetryCount(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue("x-retry-count", out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            byte byteValue => byteValue,
            byte[] bytes when bytes.Length == 4 => BitConverter.ToInt32(bytes),
            _ when int.TryParse(value.ToString(), out var parsed) => parsed,
            _ => 0
        };
    }
}
