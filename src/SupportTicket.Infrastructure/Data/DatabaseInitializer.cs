using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SupportTicket.Infrastructure.Entities;

namespace SupportTicket.Infrastructure.Data;

/// <summary>
/// Áp dụng migration và seed dữ liệu, có retry để chờ SQL Server sẵn sàng.
/// Không bao giờ xóa CSDL hiện có.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default,
        int maxAttempts = 40)
    {
        var seedOptions = configuration.GetSection(SeedOptions.SectionName).Get<SeedOptions>() ?? new SeedOptions();
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

                await db.Database.MigrateAsync(cancellationToken);
                await SeedData.SeedAsync(db, seedOptions, hasher, environment, logger, cancellationToken);
                logger.LogInformation("Cơ sở dữ liệu đã sẵn sàng (lần thử {Attempt}).", attempt);
                return;
            }
            catch (Exception exception) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                lastError = exception;
                logger.LogWarning(
                    "Chưa khởi tạo được CSDL (lần {Attempt}/{Max}): {Message}. Thử lại sau 3 giây.",
                    attempt, maxAttempts, exception.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }

        throw new InvalidOperationException("Không thể khởi tạo cơ sở dữ liệu sau nhiều lần thử.", lastError);
    }
}
