using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SupportTicket.Infrastructure.Data;

namespace SupportTicket.Infrastructure.Messaging;

/// <summary>
/// Định kỳ gửi các sự kiện trong Outbox tới RabbitMQ. Chỉ đánh dấu đã xử lý sau khi có publisher confirm.
/// Lỗi kết nối không làm mất sự kiện; lần chạy sau sẽ thử lại.
/// </summary>
public sealed class OutboxPublisherService(
    IServiceScopeFactory scopeFactory,
    RabbitMqPublisher publisher,
    IOptions<OutboxOptions> outboxOptions,
    IOptions<RabbitMqOptions> rabbitOptions,
    ILogger<OutboxPublisherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!outboxOptions.Value.Enabled || !rabbitOptions.Value.Enabled)
        {
            logger.LogInformation("Outbox publisher bị tắt bởi cấu hình.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, outboxOptions.Value.PollSeconds));
        var batchSize = Math.Clamp(outboxOptions.Value.BatchSize, 1, 100);

        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishBatchAsync(batchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Outbox publisher gặp lỗi, sẽ thử lại sau {Seconds} giây.", interval.TotalSeconds);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PublishBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var pending = await db.OutboxMessages
            .Where(x => x.ProcessedAt == null && x.NextAttemptAt <= now)
            .OrderBy(x => x.OccurredAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        var published = 0;
        foreach (var message in pending)
        {
            try
            {
                await publisher.PublishAsync(
                    message.Id, message.RoutingKey, message.EventType, message.Payload, cancellationToken);

                message.ProcessedAt = DateTime.UtcNow;
                message.Error = null;
                published++;
                logger.LogInformation(
                    "Đã gửi sự kiện Outbox {MessageId} ({EventType}, {RoutingKey}).",
                    message.Id, message.EventType, message.RoutingKey);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                message.RetryCount++;
                message.Error = Truncate(exception.Message, 1900);
                message.NextAttemptAt = DateTime.UtcNow.AddSeconds(
                    Math.Min(60, Math.Pow(2, Math.Min(message.RetryCount, 5))));
                logger.LogWarning(
                    exception,
                    "Gửi sự kiện Outbox {MessageId} thất bại (lần {Retry}). Sẽ thử lại lúc {NextAttemptAt:O}.",
                    message.Id, message.RetryCount, message.NextAttemptAt);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (published > 0)
        {
            logger.LogInformation("Outbox: đã gửi {Count}/{Total} sự kiện trong lô.", published, pending.Count);
        }
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
