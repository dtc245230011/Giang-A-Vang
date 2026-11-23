using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SupportTicket.Infrastructure.Data;
using SupportTicket.Infrastructure.Entities;
using SupportTicket.Infrastructure.Messaging;

namespace SupportTicket.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddSupportTicketInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = ResolveConnectionString(configuration);

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));

        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<Assignments.AssignmentProcessor>();
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<RabbitMqPublisher>();
        services.AddHostedService<OutboxPublisherService>();

        return services;
    }

    /// <summary>
    /// Ưu tiên connection string đầy đủ. Nếu chưa có, dựng từ MSSQL_SA_PASSWORD để tiện chạy cục bộ.
    /// </summary>
    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var password = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD")
                       ?? configuration["MssqlSaPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Chưa cấu hình chuỗi kết nối. Đặt ConnectionStrings__DefaultConnection hoặc biến MSSQL_SA_PASSWORD " +
                "(ví dụ: nạp từ file .env) trước khi chạy.");
        }

        var host = configuration["MssqlHost"] ?? "localhost";
        var port = configuration["MssqlPort"] ?? "1433";
        var database = configuration["DatabaseName"] ?? "SupportTicketDb";

        return $"Server={host},{port};Database={database};User Id=sa;Password={password};" +
               "TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True";
    }
}
