using System.Net;
using System.Net.Http.Json;

namespace SupportTicket.Tests;

[Collection(ApiCollection.Name)]
public sealed class AdminApiTests(SupportTicketApiFactory factory)
{
    [IntegrationFact]
    public async Task NonAdmin_CannotUseAdminEndpoints()
    {
        using var customer = await factory.CreateClientForAsync(TestAccounts.Customer);
        using var agent = await factory.CreateClientForAsync(TestAccounts.TechAgent);

        using var users = await customer.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);

        using var createTeam = await agent.PostAsJsonAsync("/api/teams", new
        {
            code = "HACK" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            name = "Nhóm không được phép"
        });
        Assert.Equal(HttpStatusCode.Forbidden, createTeam.StatusCode);

        using var agents = await customer.GetAsync("/api/users/agents");
        Assert.Equal(HttpStatusCode.Forbidden, agents.StatusCode);
    }

    [IntegrationFact]
    public async Task Admin_CreatesAgentWithTeam_ThenDeactivationBlocksLogin()
    {
        using var admin = await factory.CreateClientForAsync(TestAccounts.Admin);
        var teams = await admin.GetFromJsonAsync<List<TeamListResponse>>("/api/teams?includeInactive=true");
        var accounting = Assert.Single(teams!, x => x.Code == "ACCT");

        var email = $"agent_{Guid.NewGuid():N}@example.com";
        using var createResponse = await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Nhân Viên Kiểm Thử",
            email,
            password = SupportTicketApiFactory.SeedPassword,
            role = "Agent",
            teamId = accounting.Id,
            isLead = false
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<AdminUserResponse>();
        Assert.NotNull(created);
        Assert.Equal("Agent", created.Role);
        Assert.Contains(created.Teams, x => x.Code == "ACCT");

        using var newAgentClient = await factory.CreateClientForAsync(email);
        var me = await newAgentClient.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.Equal(created.Id, me!.Id);

        using var deactivate = await admin.PatchAsJsonAsync($"/api/users/{created.Id}", new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        using var anonymous = factory.CreateClient();
        using var login = await anonymous.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = SupportTicketApiFactory.SeedPassword
        });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
    }

    [IntegrationFact]
    public async Task Admin_CreatesTeamAndManagesMembers()
    {
        using var admin = await factory.CreateClientForAsync(TestAccounts.Admin);
        var code = "T" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        using var createTeam = await admin.PostAsJsonAsync("/api/teams", new
        {
            code,
            name = "Nhóm kiểm thử " + code,
            description = "Nhóm tạo bởi kiểm thử tích hợp."
        });
        Assert.Equal(HttpStatusCode.Created, createTeam.StatusCode);
        var team = await createTeam.Content.ReadFromJsonAsync<TeamListResponse>();
        Assert.NotNull(team);

        var agentEmail = $"member_{Guid.NewGuid():N}@example.com";
        using var createUser = await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Thành Viên Kiểm Thử",
            email = agentEmail,
            password = SupportTicketApiFactory.SeedPassword,
            role = "Agent"
        });
        Assert.Equal(HttpStatusCode.Created, createUser.StatusCode);
        var agent = await createUser.Content.ReadFromJsonAsync<AdminUserResponse>();

        using var addMember = await admin.PutAsJsonAsync($"/api/teams/{team!.Id}/members/{agent!.Id}", new { isLead = true });
        Assert.Equal(HttpStatusCode.NoContent, addMember.StatusCode);

        var detail = await admin.GetFromJsonAsync<TeamDetailResponse>($"/api/teams/{team.Id}");
        Assert.NotNull(detail);
        var member = Assert.Single(detail.Members);
        Assert.Equal(agent.Id, member.UserId);
        Assert.True(member.IsLead);

        using var update = await admin.PatchAsJsonAsync($"/api/teams/{team.Id}", new
        {
            name = "Nhóm đã đổi tên " + code,
            isActive = false
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var afterUpdate = await admin.GetFromJsonAsync<TeamDetailResponse>($"/api/teams/{team.Id}");
        Assert.Equal("Nhóm đã đổi tên " + code, afterUpdate!.Name);
        Assert.False(afterUpdate.IsActive);

        using var removeMember = await admin.DeleteAsync($"/api/teams/{team.Id}/members/{agent.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removeMember.StatusCode);

        var afterRemove = await admin.GetFromJsonAsync<TeamDetailResponse>($"/api/teams/{team.Id}");
        Assert.Empty(afterRemove!.Members);
    }

    [IntegrationFact]
    public async Task Admin_TeamAndUserLists_ReturnOnlyRealData()
    {
        using var admin = await factory.CreateClientForAsync(TestAccounts.Admin);
        var teams = await admin.GetFromJsonAsync<List<TeamListResponse>>("/api/teams?includeInactive=true");
        Assert.NotNull(teams);
        Assert.Contains(teams, x => x.Code == "TECH" && x.MemberCount >= 1);
        Assert.Contains(teams, x => x.Code == "GENERAL");

        var agents = await admin.GetFromJsonAsync<List<AgentResponse>>("/api/users/agents");
        Assert.NotNull(agents);
        Assert.Contains(agents, x => x.Email == TestAccounts.TechAgent);
        Assert.All(agents, x => Assert.False(string.IsNullOrWhiteSpace(x.Email)));
    }

    private sealed record TeamDetailResponse(
        Guid Id, string Code, string Name, string? Description, bool IsActive, List<TeamMemberResponse> Members);

    private sealed record TeamMemberResponse(
        Guid UserId, string FullName, string Email, string Role, bool IsActive, bool IsLead, DateTime JoinedAt);

    private sealed record AgentResponse(
        Guid Id, string FullName, string Email, List<TeamResponse> Teams);
}
