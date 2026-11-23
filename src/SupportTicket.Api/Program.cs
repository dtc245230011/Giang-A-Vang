using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SupportTicket.Api.Dtos;
using SupportTicket.Api.Errors;
using SupportTicket.Api.Health;
using SupportTicket.Api.Realtime;
using SupportTicket.Api.Security;
using SupportTicket.Api.Serialization;
using SupportTicket.Api.Services;
using SupportTicket.Infrastructure;
using SupportTicket.Infrastructure.Data;
using SupportTicket.Infrastructure.Entities;

var builder = WebApplication.CreateBuilder(args);

// Khóa JWT không nằm trong mã nguồn. Ở môi trường Development, nếu thiếu khóa sẽ sinh khóa tạm.
var generatedJwtKey = false;
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Thiếu Jwt:Key. Hãy đặt biến môi trường Jwt__Key với khóa tối thiểu 32 byte.");
    }

    jwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:Key"] = jwtKey
    });
    generatedJwtKey = true;
}

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.AddSupportTicketInfrastructure(builder.Configuration);

builder.Services.AddScoped<TicketService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<DatabaseHealthCheck>();
builder.Services.AddHostedService<NotificationConsumerService>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var problem = new ValidationProblemDetails(context.ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Dữ liệu không hợp lệ",
                Detail = "Vui lòng kiểm tra các trường được đánh dấu.",
                Instance = context.HttpContext.Request.Path
            };
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            return new BadRequestObjectResult(problem)
            {
                ContentTypes = { "application/problem+json" }
            };
        };
    });

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSignalR();

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "SupportTicket.Api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "SupportTicket.Client";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Path.StartsWithSegments("/hubs/notifications")
                    && context.Request.Query.TryGetValue("access_token", out var accessToken))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var idText = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!Guid.TryParse(idText, out var userId))
                {
                    context.Fail("Token thiếu định danh người dùng.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var isActive = await db.Users.AsNoTracking()
                    .AnyAsync(x => x.Id == userId && x.IsActive, context.HttpContext.RequestAborted);

                if (!isActive)
                {
                    context.Fail("Tài khoản đã bị vô hiệu hóa.");
                }
            }
        };
    });

builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        // Giao diện được phục vụ cùng origin nên không cần CORS; chỉ mở cho origin cấu hình rõ ràng.
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins);
        }

        policy.AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

if (generatedJwtKey)
{
    app.Logger.LogWarning(
        "Chưa cấu hình Jwt:Key. Đã sinh khóa tạm cho môi trường Development; token sẽ mất hiệu lực khi khởi động lại. " +
        "Đặt biến môi trường Jwt__Key để ổn định phiên đăng nhập.");
}

await DatabaseInitializer.InitializeAsync(
    app.Services, app.Environment, app.Configuration, app.Logger);

app.UseMiddleware<ApiExceptionMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/api/info", () => Results.Ok(new
{
    service = "SupportTicket.Api",
    status = "running",
    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
    environment = app.Environment.EnvironmentName,
    utcTime = DateTimeOffset.UtcNow
}));

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(report.Status.ToString());
    }
});

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();

// Cần thiết để WebApplicationFactory trong project test có thể khởi động API.
public partial class Program;
