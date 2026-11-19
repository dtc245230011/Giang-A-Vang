using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => Results.Redirect("/api/info"));

app.MapGet("/api/info", () => Results.Ok(new
{
    service = "SupportTicket.Api",
    status = "running",
    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
    environment = app.Environment.EnvironmentName,
    utcTime = DateTimeOffset.UtcNow
}));

app.MapHealthChecks("/health");

app.Run();

// Cần thiết để WebApplicationFactory trong project test có thể khởi động API.
public partial class Program;
