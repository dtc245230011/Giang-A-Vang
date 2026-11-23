using System.Net;
using System.Net.Http.Json;

namespace SupportTicket.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(SupportTicketApiFactory factory)
{
    [IntegrationFact]
    public async Task Login_WithSeededAdmin_ReturnsAdminRole()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestAccounts.Admin,
            password = SupportTicketApiFactory.SeedPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));
        Assert.Equal("Admin", auth.User.Role);
        Assert.Equal(TestAccounts.Admin, auth.User.Email);
    }

    [IntegrationFact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestAccounts.Customer,
            password = "SaiMatKhau@123"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [IntegrationFact]
    public async Task Register_AlwaysCreatesCustomer_AndIgnoresRequestedAdminRole()
    {
        using var client = factory.CreateClient();
        var email = $"register_{Guid.NewGuid():N}@example.com";
        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Người Đăng Ký Kiểm Thử",
            email,
            password = "MatKhau@123",
            role = "Admin"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.Equal("Customer", auth.User.Role);

        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "MatKhau@123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var loginAuth = await login.Content.ReadFromJsonAsync<AuthResponse>();
        using var meClient = factory.CreateClient();
        meClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginAuth!.Token);
        var me = await meClient.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.Equal("Customer", me!.Role);
    }

    [IntegrationFact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        using var client = factory.CreateClient();
        var email = $"duplicate_{Guid.NewGuid():N}@example.com";

        using var first = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Tài Khoản Một",
            email,
            password = "MatKhau@123"
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Tài Khoản Hai",
            email,
            password = "MatKhau@123"
        });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [IntegrationFact]
    public async Task InactiveUser_CannotLogin()
    {
        using var admin = await factory.CreateClientForAsync(TestAccounts.Admin);
        using var customer = await factory.CreateClientForAsync(TestAccounts.Customer);
        var me = await customer.GetFromJsonAsync<UserResponse>("/api/auth/me");

        try
        {
            using var deactivate = await admin.PatchAsJsonAsync($"/api/users/{me!.Id}", new { isActive = false });
            Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

            using var client = factory.CreateClient();
            using var login = await client.PostAsJsonAsync("/api/auth/login", new
            {
                email = TestAccounts.Customer,
                password = SupportTicketApiFactory.SeedPassword
            });
            Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        }
        finally
        {
            using var reactivate = await admin.PatchAsJsonAsync($"/api/users/{me!.Id}", new { isActive = true });
            Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        }
    }

    [IntegrationFact]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/tickets");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [IntegrationFact]
    public async Task Admin_CannotDeactivateOwnAccount()
    {
        using var admin = await factory.CreateClientForAsync(TestAccounts.Admin);
        var me = await admin.GetFromJsonAsync<UserResponse>("/api/auth/me");

        using var response = await admin.PatchAsJsonAsync($"/api/users/{me!.Id}", new { isActive = false });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [IntegrationFact]
    public async Task SignalRNegotiate_RequiresAuthentication()
    {
        using var anonymous = factory.CreateClient();
        using var unauthorized = await anonymous.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var customer = await factory.CreateClientForAsync(TestAccounts.Customer);
        using var authorized = await customer.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        var body = await authorized.Content.ReadAsStringAsync();
        Assert.Contains("connectionToken", body);
    }
}
