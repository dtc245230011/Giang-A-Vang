using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SupportTicket.Contracts;
using SupportTicket.Infrastructure.Assignments;
using SupportTicket.Infrastructure.Data;
using SupportTicket.Infrastructure.Messaging;

namespace SupportTicket.Worker;

/// <summary>
/// Tiến trình nền nhận TicketCreated từ RabbitMQ và áp dụng quy tắc phân công.
/// </summary>
public sealed class AssignmentWorkerService(
    RabbitMqConnection connection,
    RabbitMqPublisher publisher,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> rabbitOptions,
    ILogger<AssignmentWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!rabbitOptions.Value.Enabled)
        {
            logger.LogWarning("RabbitMQ bị tắt; Assignment Worker sẽ không xử lý message.");
            return;
        }

        await WaitForDatabaseAsync(stoppingToken);

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
                logger.LogWarning(exception, "Assignment Worker mất kết nối RabbitMQ; thử lại sau 5 giây.");
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

    private async Task WaitForDatabaseAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= 60; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (await db.Database.CanConnectAsync(stoppingToken))
                {
                    logger.LogInformation("Assignment Worker kết nối được CSDL ở lần thử {Attempt}.", attempt);
                    return;
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Chờ CSDL sẵn sàng (lần {Attempt}): {Message}", attempt, exception.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }

        throw new InvalidOperationException("Assignment Worker không kết nối được CSDL sau 60 lần thử.");
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        var rabbit = await connection.GetConnectionAsync(stoppingToken);
        await using var channel = await rabbit.CreateChannelAsync(cancellationToken: stoppingToken);
        await Topology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, 1, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) => await HandleMessageAsync(channel, args, stoppingToken);

        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, args) =>
        {
            logger.LogWarning("Assignment channel đóng: {Reason}.", args.ReplyText);
            shutdown.TrySetResult();
            return Task.CompletedTask;
        };

        await channel.BasicConsumeAsync(
            MessagingTopology.AssignmentQueue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        logger.LogInformation("Assignment Worker đang lắng nghe queue {Queue}.", MessagingTopology.AssignmentQueue);

        await Task.WhenAny(Task.Delay(Timeout.Infinite, stoppingToken), shutdown.Task);
    }

    private async Task HandleMessageAsync(
        IChannel channel, BasicDeliverEventArgs args, CancellationToken cancellationToken)
    {
        var messageId = Guid.TryParse(args.BasicProperties.MessageId, out var parsed) ? parsed : Guid.NewGuid();
        var eventType = args.BasicProperties.Type ?? string.Empty;
        var payload = Encoding.UTF8.GetString(args.Body.Span);

        try
        {
            if (eventType != EventTypes.TicketCreated)
            {
                throw new InvalidOperationException($"Assignment Worker không hỗ trợ sự kiện {eventType}.");
            }

            var message = JsonSerializer.Deserialize<TicketCreatedEvent>(payload, MessagingJson.Options)
                          ?? throw new InvalidOperationException("Payload TicketCreated không hợp lệ.");

            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<AssignmentProcessor>();
            var outcome = await processor.ProcessAsync(message, cancellationToken);

            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
            logger.LogInformation(
                "Message {MessageId} ticket {TicketNumber}: {Outcome}.",
                messageId, message.TicketNumber, outcome);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Xử lý TicketCreated {MessageId} thất bại.", messageId);
            await RetryOrDeadLetterAsync(channel, args, messageId, eventType, payload, exception, cancellationToken);
        }
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
                    routingKey: MessagingTopology.AssignmentRetryQueue,
                    messageId, eventType, payload, retryCount + 1, cancellationToken);
                logger.LogWarning(
                    "Đưa message {MessageId} vào retry queue (lần {Retry}/{Max}).",
                    messageId, retryCount + 1, MessagingTopology.MaxDeliveryAttempts);
            }
            else
            {
                await publisher.PublishRawAsync(
                    exchange: string.Empty,
                    routingKey: MessagingTopology.AssignmentDeadLetterQueue,
                    messageId, eventType, payload, retryCount + 1, cancellationToken);
                logger.LogError(
                    "Đưa message {MessageId} vào DLQ sau {Retry} lần thử. Lỗi: {Message}",
                    messageId, retryCount + 1, exception.Message);
            }

            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception publishException)
        {
            logger.LogError(publishException, "Không thể chuyển message {MessageId} sang retry/DLQ.", messageId);
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
