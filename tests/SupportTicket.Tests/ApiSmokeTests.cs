using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SupportTicket.Tests;

public sealed class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiSmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", (await response.Content.ReadAsStringAsync()).Trim());
    }

    [Fact]
    public async Task Info_ReturnsServiceMetadata()
    {
        using var client = _factory.CreateClient();
        var info = await client.GetFromJsonAsync<ServiceInfo>("/api/info");

        Assert.NotNull(info);
        Assert.Equal("SupportTicket.Api", info.Service);
        Assert.Equal("running", info.Status);
    }

    private sealed record ServiceInfo(string Service, string Status);
}
