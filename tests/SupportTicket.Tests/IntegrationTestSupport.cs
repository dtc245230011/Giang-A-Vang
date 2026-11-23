using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SupportTicket.Infrastructure.Data;

namespace SupportTicket.Tests;

/// <summary>Chỉ chạy khi có SQL Server thật: đặt RUN_INTEGRATION_TESTS=1.</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_INTEGRATION_TESTS") != "1")
        {
            Skip = "Kiểm thử tích hợp cần SQL Server thật. Đặt RUN_INTEGRATION_TESTS=1 và cấu hình MSSQL_SA_PASSWORD.";
        }
    }
}

public sealed class IntegrationTheoryAttribute : TheoryAttribute
{
    public IntegrationTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_INTEGRATION_TESTS") != "1")
        {
            Skip = "Kiểm thử tích hợp cần SQL Server thật. Đặt RUN_INTEGRATION_TESTS=1 và cấu hình MSSQL_SA_PASSWORD.";
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<SupportTicketApiFactory>
{
    public const string Name = "api-integration";
}

/// <summary>
/// Dựng API thật với một database riêng cho mỗi lần chạy test, tắt RabbitMQ/outbox consumer
/// để kiểm thử API độc lập. Database test bị xóa sau khi chạy xong.
/// </summary>
public sealed class SupportTicketApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SeedPassword = "Test@12345";

    private readonly string _connectionString;
    private readonly string _masterConnectionString;

    public SupportTicketApiFactory()
    {
        var password = ResolveSqlPassword();
        var port = ResolveSqlPort();
        DatabaseName = "SupportTicketTest_" + Guid.NewGuid().ToString("N")[..10];

        _connectionString =
            $"Server=localhost,{port};Database={DatabaseName};User Id=sa;Password={password};" +
            "TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True";
        _masterConnectionString =
            $"Server=localhost,{port};Database=master;User Id=sa;Password={password};" +
            "TrustServerCertificate=True;Encrypt=False;Pooling=False";

        // Program đọc cấu hình ngay khi dựng builder, trước khi ConfigureAppConfiguration của
        // WebApplicationFactory có hiệu lực. Đặt biến môi trường để bảo đảm test không bao giờ
        // chạm vào CSDL phát triển.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _connectionString);
        Environment.SetEnvironmentVariable("Jwt__Key", "integration-test-key-0123456789abcdefghijklmnopqrstuvwxyz");
        Environment.SetEnvironmentVariable("RabbitMq__Enabled", "false");
        Environment.SetEnvironmentVariable("Outbox__Enabled", "false");
        Environment.SetEnvironmentVariable("Notifications__ConsumerEnabled", "false");
        Environment.SetEnvironmentVariable("Seed__DefaultPassword", SeedPassword);
        Environment.SetEnvironmentVariable("Seed__IncludeDemoTickets", "false");
    }

    public string DatabaseName { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Jwt:Key"] = "integration-test-key-0123456789abcdefghijklmnopqrstuvwxyz",
                ["Jwt:Issuer"] = "SupportTicket.Api",
                ["Jwt:Audience"] = "SupportTicket.Client",
                ["RabbitMq:Enabled"] = "false",
                ["Outbox:Enabled"] = "false",
                ["Notifications:ConsumerEnabled"] = "false",
                ["Seed:DefaultPassword"] = SeedPassword,
                ["Seed:IncludeDemoTickets"] = "false"
            });
        });
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/health");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Không khởi động được API kiểm thử (health = {(int)response.StatusCode}).");
        }

        var actualDatabase = await WithDbAsync(async db =>
        {
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT DB_NAME()";
            return (string)(await command.ExecuteScalarAsync())!;
        });

        if (!string.Equals(actualDatabase, DatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Test đang trỏ sai CSDL: mong đợi {DatabaseName}, thực tế {actualDatabase}.");
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        SqlConnection.ClearAllPools();

        try
        {
            await using var connection = new SqlConnection(_masterConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"IF DB_ID(N'{DatabaseName}') IS NOT NULL BEGIN " +
                $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{DatabaseName}]; END";
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            // Không che giấu kết quả test vì lỗi dọn dẹp database tạm.
        }
    }

    public async Task<HttpClient> CreateClientForAsync(string email, string? password = null)
    {
        var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = password ?? SeedPassword
        });

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Đăng nhập kiểm thử thất bại cho {email}: {body}");
        }

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(db);
    }

    private static string ResolveSqlPassword()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        foreach (var line in ReadDotEnvLines())
        {
            if (line.StartsWith("MSSQL_SA_PASSWORD=", StringComparison.Ordinal))
            {
                return line["MSSQL_SA_PASSWORD=".Length..].Trim().Trim('"');
            }
        }

        throw new InvalidOperationException(
            "Không tìm thấy mật khẩu SQL Server. Đặt biến MSSQL_SA_PASSWORD hoặc tạo file .env từ .env.example.");
    }

    private static string ResolveSqlPort()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("SQLSERVER_PORT");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        foreach (var line in ReadDotEnvLines())
        {
            if (line.StartsWith("SQLSERVER_PORT=", StringComparison.Ordinal))
            {
                return line["SQLSERVER_PORT=".Length..].Trim().Trim('"');
            }
        }

        return "1433";
    }

    private static IEnumerable<string> ReadDotEnvLines()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, ".env");
            if (File.Exists(path))
            {
                return File.ReadAllLines(path)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0 && !x.StartsWith('#'))
                    .ToList();
            }

            directory = directory.Parent;
        }

        return Array.Empty<string>();
    }
}

public static class TestAccounts
{
    public const string Admin = "admin@support.local";
    public const string TechAgent = "agent.tech@support.local";
    public const string AccountAgent = "agent.acct@support.local";
    public const string CskhAgent = "agent.cskh@support.local";
    public const string GeneralAgent = "agent.general@support.local";
    public const string Customer = "customer@support.local";
    public const string Customer2 = "customer2@support.local";
}

public sealed record AuthResponse(string Token, DateTime ExpiresAt, UserResponse User);
public sealed record UserResponse(Guid Id, string FullName, string Email, string Role, bool IsActive, DateTime CreatedAt);
public sealed record PersonResponse(Guid Id, string FullName, string Email);
public sealed record TeamResponse(Guid Id, string Code, string Name);
public sealed record TeamListResponse(Guid Id, string Code, string Name, string? Description, bool IsActive, int MemberCount);

public sealed record TicketListResponse(
    Guid Id, string TicketNumber, string Title, string Category, string Priority, string Status,
    PersonResponse Requester, TeamResponse? AssignedTeam, PersonResponse? AssignedUser,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? ResolvedAt);

public sealed record TicketDetailResponse(
    Guid Id, string TicketNumber, string Title, string Description, string Category, string Priority,
    string Status, PersonResponse Requester, TeamResponse? AssignedTeam, PersonResponse? AssignedUser,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? ResolvedAt, string RowVersion,
    bool CanChangeStatus, bool CanAssign, List<string> AllowedNextStatuses);

public sealed record PagedResponse<T>(List<T> Items, int Page, int PageSize, int TotalItems, int TotalPages);
public sealed record HistoryResponse(
    Guid Id, string FieldChanged, string? OldValue, string? NewValue, string? Note,
    DateTime ChangedAt, Guid? ChangedByUserId, string? ChangedByName);
public sealed record AdminUserResponse(
    Guid Id, string FullName, string Email, string Role, bool IsActive, DateTime CreatedAt,
    List<TeamResponse> Teams);
