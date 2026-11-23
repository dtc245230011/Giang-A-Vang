using Microsoft.Extensions.Diagnostics.HealthChecks;
using SupportTicket.Infrastructure.Data;

namespace SupportTicket.Api.Health;

public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("SQL Server kết nối được.")
                : HealthCheckResult.Unhealthy("Không kết nối được SQL Server.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Lỗi kiểm tra SQL Server.", exception);
        }
    }
}
