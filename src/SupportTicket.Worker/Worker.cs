namespace SupportTicket.Worker;

public sealed class Worker(ILogger<Worker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var heartbeatSeconds = Math.Max(5, configuration.GetValue("Worker:HeartbeatSeconds", 30));

        logger.LogInformation(
            "Assignment Worker started. Heartbeat every {HeartbeatSeconds}s. " +
            "RabbitMQ integration will be added in week 3.",
            heartbeatSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(heartbeatSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            logger.LogInformation("Assignment Worker heartbeat at {Time}", DateTimeOffset.UtcNow);
        }
    }
}
