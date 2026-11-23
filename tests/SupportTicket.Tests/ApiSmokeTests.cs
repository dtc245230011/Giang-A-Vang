using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SupportTicket.Contracts;
using SupportTicket.Infrastructure.Messaging;

namespace SupportTicket.Tests;

[Collection(ApiCollection.Name)]
public sealed class ApiSmokeTests(SupportTicketApiFactory factory)
{
    [IntegrationFact]
    public async Task Health_ReturnsHealthy()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", (await response.Content.ReadAsStringAsync()).Trim());
    }

    [IntegrationFact]
    public async Task Info_ReturnsServiceMetadata()
    {
        using var client = factory.CreateClient();
        var info = await client.GetFromJsonAsync<ServiceInfo>("/api/info");

        Assert.NotNull(info);
        Assert.Equal("SupportTicket.Api", info.Service);
        Assert.Equal("running", info.Status);
    }

    [IntegrationFact]
    public async Task Frontend_IsServedByApi()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/login.html");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Đăng nhập", html);
        Assert.Contains("Support Ticket", html);
    }

    [IntegrationFact]
    public async Task RequiredTicketPaths_WorkWithAndWithoutApiPrefix()
    {
        using var customer = await factory.CreateClientForAsync(TestAccounts.Customer);
        var created = await CreateTicketAsync(customer);

        var direct = await customer.GetFromJsonAsync<PagedResponse<TicketListResponse>>("/tickets");
        var prefixed = await customer.GetFromJsonAsync<PagedResponse<TicketListResponse>>("/api/tickets");
        Assert.NotNull(direct);
        Assert.NotNull(prefixed);
        Assert.Equal(direct.TotalItems, prefixed.TotalItems);

        using var directDetail = await customer.GetAsync($"/tickets/{created.Id}");
        using var prefixedDetail = await customer.GetAsync($"/api/tickets/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, directDetail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, prefixedDetail.StatusCode);
    }

    [IntegrationFact]
    public async Task MessagingTopology_NamesMatchDesign()
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>().Value;
        Assert.False(options.Enabled, "Kiểm thử API phải tắt RabbitMQ.");

        Assert.Equal("support-tickets", MessagingTopology.Exchange);
        Assert.Equal("ticket.created", RoutingKeys.TicketCreated);
        Assert.Equal(3, MessagingTopology.MaxDeliveryAttempts);
    }

    private static async Task<TicketDetailResponse> CreateTicketAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/tickets", new
        {
            title = "Kiểm thử tương thích đường dẫn API " + Guid.NewGuid().ToString("N")[..6],
            description = "Nội dung kiểm thử tương thích đường dẫn có và không có tiền tố api.",
            category = "Account",
            priority = "Medium"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TicketDetailResponse>();
        Assert.NotNull(created);
        return created;
    }

    private sealed record ServiceInfo(string Service, string Status);
}
