using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupportTicket.Contracts;
using SupportTicket.Infrastructure.Assignments;
using SupportTicket.Infrastructure.Data;

namespace SupportTicket.Tests;

[Collection(ApiCollection.Name)]
public sealed class TicketFlowTests(SupportTicketApiFactory factory)
{
    [IntegrationFact]
    public async Task CreateTicket_PersistsEntityHistoryAndOutboxEvent()
    {
        using var customer = await factory.CreateClientForAsync(TestAccounts.Customer);
        var me = await customer.GetFromJsonAsync<UserResponse>("/api/auth/me");

        var ticket = await CreateTicketAsync(customer, "Account", "High");

        Assert.Equal("New", ticket.Status);
        Assert.Equal("Account", ticket.Category);
        Assert.Equal("High", ticket.Priority);
        Assert.Equal(me!.Id, ticket.Requester.Id);
        Assert.StartsWith("TK-", ticket.TicketNumber);
        Assert.False(string.IsNullOrWhiteSpace(ticket.RowVersion));
        Assert.False(ticket.CanChangeStatus);
        Assert.False(ticket.CanAssign);
        Assert.Empty(ticket.AllowedNextStatuses);

        var history = await customer.GetFromJsonAsync<List<HistoryResponse>>($"/api/tickets/{ticket.Id}/history");
        Assert.NotNull(history);
        var createdHistory = Assert.Single(history);
        Assert.Equal("Status", createdHistory.FieldChanged);
        Assert.Null(createdHistory.OldValue);
        Assert.Equal("New", createdHistory.NewValue);
        Assert.Equal(me.Id, createdHistory.ChangedByUserId);

        var outboxCount = await factory.WithDbAsync(db => db.OutboxMessages
            .CountAsync(x => x.EventType == EventTypes.TicketCreated
                             && x.ProcessedAt == null
                             && x.Payload.Contains(ticket.Id.ToString())));
        Assert.Equal(1, outboxCount);

        var storedRequester = await factory.WithDbAsync(db => db.Tickets
            .Where(x => x.Id == ticket.Id)
            .Select(x => x.RequesterId)
            .SingleAsync());
        Assert.Equal(me.Id, storedRequester);

        var page = await customer.GetFromJsonAsync<PagedResponse<TicketListResponse>>(
            "/api/tickets?search=" + Uri.EscapeDataString(ticket.TicketNumber));
        Assert.NotNull(page);
        Assert.Contains(page.Items, x => x.Id == ticket.Id);
    }

    [IntegrationFact]
    public async Task CreateTicket_InvalidPayload_ReturnsBadRequest()
    {
        using var customer = await factory.CreateClientForAsync(TestAccounts.Customer);

        using var shortTitle = await customer.PostAsJsonAsync("/api/tickets", new
        {
            title = "abc",
            description = "Mô tả đủ dài cho kiểm thử.",
            category = "Account"
        });
        Assert.Equal(HttpStatusCode.BadRequest, shortTitle.StatusCode);

        using var shortDescription = await customer.PostAsJsonAsync("/api/tickets", new
        {
            title = "Tiêu đề hợp lệ cho kiểm thử",
            description = "ngan",
            category = "Account"
        });
        Assert.Equal(HttpStatusCode.BadRequest, shortDescription.StatusCode);

        using var invalidCategory = await customer.PostAsJsonAsync("/api/tickets", new
        {
            title = "Tiêu đề hợp lệ cho kiểm thử",
            description = "Mô tả đủ dài cho kiểm thử.",
            category = "KhongTonTai"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCategory.StatusCode);
    }

    [IntegrationFact]
    public async Task Customer_CannotReadAnotherCustomersTicket()
    {
        using var customer1 = await factory.CreateClientForAsync(TestAccounts.Customer);
        using var customer2 = await factory.CreateClientForAsync(TestAccounts.Customer2);
        using var admin = await factory.CreateClientForAsync(TestAccounts.Admin);

        var customer1Me = await customer1.GetFromJsonAsync<UserResponse>("/api/auth/me");
        using var customer2MeResponse = await customer2.GetAsync("/api/auth/me");
        var customer2Me = await customer2MeResponse.Content.ReadFromJsonAsync<UserResponse>();

        var ticket = await CreateTicketAsync(customer1);

        using var detail = await customer2.GetAsync($"/api/tickets/{ticket.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);

        var otherList = await customer2.GetFromJsonAsync<PagedResponse<TicketListResponse>>(
            "/api/tickets?search=" + Uri.EscapeDataString(ticket.TicketNumber));
        Assert.NotNull(otherList);
        Assert.DoesNotContain(otherList.Items, x => x.Id == ticket.Id);

        using var otherUsersTickets = await customer2.GetAsync($"/api/users/{customer1Me!.Id}/tickets");
        Assert.Equal(HttpStatusCode.Forbidden, otherUsersTickets.StatusCode);

        using var adminUsersTickets = await admin.GetAsync($"/api/users/{customer1Me.Id}/tickets");
        Assert.Equal(HttpStatusCode.OK, adminUsersTickets.StatusCode);

        using var ownTickets = await customer2.GetAsync($"/api/users/{customer2Me!.Id}/tickets");
        Assert.Equal(HttpStatusCode.OK, ownTickets.StatusCode);
    }

    [IntegrationFact]
    public async Task Agent_CannotSeeUnassignedNewTicket()
    {
        using var customer = await factory.CreateClientForAsync(TestAccounts.Customer);
        using var agent = await factory.CreateClientForAsync(TestAccounts.TechAgent);
        using var admin = await factory.CreateClientForAsync(TestAccounts.Admin);

        var ticket = await CreateTicketAsync(customer);

        using var agentDetail = await agent.GetAsync($"/api/tickets/{ticket.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, agentDetail.StatusCode);

        var agentList = await agent.GetFromJsonAsync<PagedResponse<TicketListResponse>>(
            "/api/tickets?search=" + Uri.EscapeDataString(ticket.TicketNumber));
        Assert.DoesNotContain(agentList!.Items, x => x.Id == ticket.Id);

        using var adminDetail = await admin.GetAsync($"/api/tickets/{ticket.Id}");
        Assert.Equal(HttpStatusCode.OK, adminDetail.StatusCode);
    }

    [IntegrationFact]
    public async Task AdminAssigns_ThenAgentStartsAndResolvesTicket()
    {
        using var customer = await factory.CreateClientForAsync(TestAccounts.Customer);
        using var admin = await factory.CreateClientForAsync(TestAccounts.Admin);
        using var agent = await factory.CreateClientForAsync(TestAccounts.TechAgent);
        var agentMe = await agent.GetFromJsonAsync<UserResponse>("/api/auth/me");

        var ticket = await CreateTicketAsync(customer, "Account", "High");
        var assigned = await AssignAsync(admin, ticket, "TECH", TestAccounts.TechAgent);

        Assert.Equal("Assigned", assigned.Status);
        Assert.Equal("TECH", assigned.AssignedTeam!.Code);
        Assert.Equal(agentMe!.Id, assigned.AssignedUser!.Id);
        Assert.True(assigned.CanChangeStatus);
        Assert.True(assigned.CanAssign);
        Assert.Equal(new[] { "InProgress" }, assigned.AllowedNextStatuses);

        var history = await admin.GetFromJsonAsync<List<HistoryResponse>>($"/api/tickets/{ticket.Id}/history");
        Assert.NotNull(history);
        Assert.Contains(history, x => x.FieldChanged == "AssignedTeamId" && x.NewValue == "TECH");
        Assert.Contains(history, x => x.FieldChanged == "AssignedUserId" && x.NewValue == agentMe.FullName);
        Assert.Contains(history, x => x.FieldChanged == "Status" && x.OldValue == "New" && x.NewValue == "Assigned");

        var outboxCount = await factory.WithDbAsync(db => db.OutboxMessages
            .CountAsync(x => x.EventType == EventTypes.TicketAssigned && x.Payload.Contains(ticket.Id.ToString())));
        Assert.Equal(1, outboxCount);

        using var agentDetailResponse = await agent.GetAsync($"/api/tickets/{ticket.Id}");
        Assert.Equal(HttpStatusCode.OK, agentDetailResponse.StatusCode);
        var agentDetail = await agentDetailResponse.Content.ReadFromJsonAsync<TicketDetailResponse>();
        Assert.True(agentDetail!.CanChangeStatus);

        using var startResponse = await agent.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/status", new
        {
            status = "InProgress",
            note = "Nhân viên bắt đầu xử lý.",
            rowVersion = agentDetail.RowVersion
        });
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var started = await startResponse.Content.ReadFromJsonAsync<TicketDetailResponse>();
        Assert.Equal("InProgress", started!.Status);

        using var resolveResponse = await agent.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/status", new
        {
            status = "Resolved",
            note = "Đã giải quyết xong.",
            rowVersion = started.RowVersion
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
        var resolved = await resolveResponse.Content.ReadFromJsonAsync<TicketDetailResponse>();
        Assert.Equal("Resolved", resolved!.Status);
        Assert.NotNull(resolved.ResolvedAt);
        Assert.Empty(resolved.AllowedNextStatuses);
        Assert.False(resolved.CanChangeStatus);

        var finalHistory = await customer.GetFromJsonAsync<List<HistoryResponse>>($"/api/tickets/{ticket.Id}/history");
        Assert.NotNull(finalHistory);
        Assert.Contains(finalHistory, x => x.FieldChanged == "Status" && x.OldValue == "Assigned" && x.NewValue == "InProgress");
        Assert.Contains(finalHistory, x => x.FieldChanged == "Status" && x.OldValue == "InProgress" && x.NewValue == "Resolved");

        using var afterResolved = await agent.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/status", new
        {
            status = "InProgress",
            rowVersion = resolved.RowVersion
        });
        Assert.Equal(HttpStatusCode.Conflict, afterResolved.StatusCode);
    }

    [IntegrationFact]
    public async Task InvalidTransitions_AreRejected()
    {
        using var customer = await factory.CreateClientForAsync(TestAccounts.Customer);
        using var admin = await factory.CreateClientForAsync(TestAccounts.Admin);
        using var agent = await factory.CreateClientForAsync(TestAccounts.TechAgent);

        var ticket = await CreateTicketAsync(customer);

        using var skipAssignment = await admin.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/status", new
        {
            status = "InProgress",
            rowVersion = ticket.RowVersion
        });
        Assert.Equal(HttpStatusCode.Conflict, skipAssignment.StatusCode);

        using var customerStatus = await customer.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/status", new
        {
            status = "Assigned",
            rowVersion = ticket.RowVersion
        });
        Assert.Equal(HttpStatusCode.Forbidden, customerStatus.StatusCode);

        var assigned = await AssignAsync(admin, ticket, "TECH", TestAccounts.TechAgent);
        using var skipInProgress = await agent.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/status", new
        {
            status = "Resolved",
            rowVersion = assigned.RowVersion
        });
        Assert.Equal(HttpStatusCode.Conflict, skipInProgress.StatusCode);

        using var customerAssign = await customer.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new
        {
            teamId = assigned.AssignedTeam!.Id,
            assignedUserId = assigned.AssignedUser!.Id,
            rowVersion = assigned.RowVersion
        });
        Assert.Equal(HttpStatusCode.Forbidden, customerAssign.StatusCode);
    }

    [IntegrationFact]
    public async Task ConcurrentStatusUpdates_OneSucceedsAndOneConflicts()
    {
        using var customer = await factory.CreateClientForAsync(TestAccounts.Customer);
        using var admin = await factory.CreateClientForAsync(TestAccounts.Admin);
        using var agent = await factory.CreateClientForAsync(TestAccounts.TechAgent);

        var ticket = await CreateTicketAsync(customer);
        var assigned = await AssignAsync(admin, ticket, "TECH", TestAccounts.TechAgent);

        async Task<HttpResponseMessage> StartAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tickets/{ticket.Id}/status")
            {
                Content = JsonContent.Create(new
                {
                    status = "InProgress",
                    note = "Cập nhật đồng thời",
                    rowVersion = assigned.RowVersion
                })
            };
            return await agent.SendAsync(request);
        }

        var responses = await Task.WhenAll(StartAsync(), StartAsync());
        try
        {
            Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        var latest = await agent.GetFromJsonAsync<TicketDetailResponse>($"/api/tickets/{ticket.Id}");
        Assert.Equal("InProgress", latest!.Status);
    }

    [IntegrationFact]
    public async Task List_SupportsSearchFiltersAndPagination()
    {
        using var customer = await factory.CreateClientForAsync(TestAccounts.Customer);
        var token = "LOC" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var first = await CreateTicketAsync(customer, "Account", "High", "Kiểm thử lọc " + token + " A");
        var second = await CreateTicketAsync(customer, "Payment", "Low", "Kiểm thử lọc " + token + " B");
        var third = await CreateTicketAsync(customer, "Delivery", "Medium", "Kiểm thử lọc " + token + " C");

        var page1 = await customer.GetFromJsonAsync<PagedResponse<TicketListResponse>>(
            $"/api/tickets?search={token}&page=1&pageSize=2");
        Assert.NotNull(page1);
        Assert.Equal(3, page1.TotalItems);
        Assert.Equal(2, page1.PageSize);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(2, page1.Items.Count);

        var page2 = await customer.GetFromJsonAsync<PagedResponse<TicketListResponse>>(
            $"/api/tickets?search={token}&page=2&pageSize=2");
        Assert.Single(page2!.Items);
        Assert.Empty(page1.Items.Select(x => x.Id).Intersect(page2.Items.Select(x => x.Id)));

        var filtered = await customer.GetFromJsonAsync<PagedResponse<TicketListResponse>>(
            $"/api/tickets?search={token}&category=Delivery&status=New&priority=Medium&unassigned=true");
        Assert.NotNull(filtered);
        Assert.Single(filtered.Items);
        Assert.Equal(third.Id, filtered.Items[0].Id);

        var byNumber = await customer.GetFromJsonAsync<PagedResponse<TicketListResponse>>(
            "/api/tickets?search=" + Uri.EscapeDataString(first.TicketNumber));
        Assert.Contains(byNumber!.Items, x => x.Id == first.Id);

        Assert.Contains(new[] { first.Id, second.Id, third.Id }, id => id == first.Id);
    }

    [IntegrationTheory]
    [InlineData("Account", "TECH", TestAccounts.TechAgent)]
    [InlineData("Payment", "ACCT", TestAccounts.AccountAgent)]
    [InlineData("Delivery", "CSKH", TestAccounts.CskhAgent)]
    [InlineData("Other", "GENERAL", TestAccounts.GeneralAgent)]
    public async Task WorkerProcessor_AssignsCategoryToExpectedTeam(
        string category, string expectedTeamCode, string expectedLeadEmail)
    {
        using var customer = await factory.CreateClientForAsync(TestAccounts.Customer);
        var me = await customer.GetFromJsonAsync<UserResponse>("/api/auth/me");
        var ticket = await CreateTicketAsync(customer, category, "Medium");

        var message = new TicketCreatedEvent(
            Guid.NewGuid(), ticket.Id, ticket.TicketNumber,
            Enum.Parse<TicketCategory>(category), TicketPriority.Medium, me!.Id, DateTime.UtcNow);

        await using var scope = factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<AssignmentProcessor>();
        var outcome = await processor.ProcessAsync(message, CancellationToken.None);
        Assert.Equal(AssignmentOutcome.Assigned, outcome);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Tickets.AsNoTracking()
            .Include(x => x.AssignedTeam)
            .Include(x => x.AssignedUser)
            .SingleAsync(x => x.Id == ticket.Id);

        Assert.Equal("Assigned", stored.Status.ToString());
        Assert.Equal(expectedTeamCode, stored.AssignedTeam!.Code);
        Assert.NotNull(stored.AssignedUser);
        Assert.Equal(expectedLeadEmail, stored.AssignedUser!.Email);
        Assert.Equal(4, await db.TicketHistories.CountAsync(x => x.TicketId == ticket.Id));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(
            x => x.EventType == EventTypes.TicketAssigned && x.Payload.Contains(ticket.Id.ToString())));
    }

    [IntegrationFact]
    public async Task WorkerProcessor_IsIdempotentForRedeliveredMessages()
    {
        using var customer = await factory.CreateClientForAsync(TestAccounts.Customer);
        var me = await customer.GetFromJsonAsync<UserResponse>("/api/auth/me");
        var ticket = await CreateTicketAsync(customer, "Account", "Medium");

        var messageId = Guid.NewGuid();
        var message = new TicketCreatedEvent(
            messageId, ticket.Id, ticket.TicketNumber,
            TicketCategory.Account, TicketPriority.Medium, me!.Id, DateTime.UtcNow);

        await using var scope = factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<AssignmentProcessor>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(AssignmentOutcome.Assigned, await processor.ProcessAsync(message, CancellationToken.None));
        var historyAfterFirst = await db.TicketHistories.CountAsync(x => x.TicketId == ticket.Id);
        var assignedEvents = await db.OutboxMessages.CountAsync(
            x => x.EventType == EventTypes.TicketAssigned && x.Payload.Contains(ticket.Id.ToString()));
        Assert.Equal(4, historyAfterFirst);
        Assert.Equal(1, assignedEvents);

        Assert.Equal(AssignmentOutcome.Duplicate, await processor.ProcessAsync(message, CancellationToken.None));
        Assert.Equal(historyAfterFirst, await db.TicketHistories.CountAsync(x => x.TicketId == ticket.Id));
        Assert.Equal(assignedEvents, await db.OutboxMessages.CountAsync(
            x => x.EventType == EventTypes.TicketAssigned && x.Payload.Contains(ticket.Id.ToString())));

        // Message mới cho ticket đã phân công không được ghi đè phân công thủ công.
        var redelivered = message with { MessageId = Guid.NewGuid() };
        Assert.Equal(AssignmentOutcome.AlreadyAssigned, await processor.ProcessAsync(redelivered, CancellationToken.None));
        Assert.Equal(historyAfterFirst, await db.TicketHistories.CountAsync(x => x.TicketId == ticket.Id));
    }

    [IntegrationFact]
    public async Task WorkerProcessor_MissingTeamFailsWithoutPartialWrite_ThenRetrySucceeds()
    {
        using var customer = await factory.CreateClientForAsync(TestAccounts.Customer);
        var me = await customer.GetFromJsonAsync<UserResponse>("/api/auth/me");
        var ticket = await CreateTicketAsync(customer, "Other", "Low");

        await factory.WithDbAsync(async db =>
        {
            var team = await db.Teams.SingleAsync(x => x.Code == "GENERAL");
            team.IsActive = false;
            await db.SaveChangesAsync();
            return true;
        });

        var message = new TicketCreatedEvent(
            Guid.NewGuid(), ticket.Id, ticket.TicketNumber,
            TicketCategory.Other, TicketPriority.Low, me!.Id, DateTime.UtcNow);

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<AssignmentProcessor>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => processor.ProcessAsync(message, CancellationToken.None));

            var afterFailure = await db.Tickets.AsNoTracking().SingleAsync(x => x.Id == ticket.Id);
            Assert.Equal("New", afterFailure.Status.ToString());
            Assert.Null(afterFailure.AssignedTeamId);
            Assert.False(await db.InboxMessages.AnyAsync(x => x.MessageId == message.MessageId));

            await db.Teams.Where(x => x.Code == "GENERAL")
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsActive, true));

            var outcome = await processor.ProcessAsync(message, CancellationToken.None);
            Assert.Equal(AssignmentOutcome.Assigned, outcome);
        }
        finally
        {
            await factory.WithDbAsync(async db =>
            {
                await db.Teams.Where(x => x.Code == "GENERAL")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsActive, true));
                return true;
            });
        }
    }

    private static async Task<TicketDetailResponse> CreateTicketAsync(
        HttpClient client, string category = "Account", string priority = "Medium", string? title = null)
    {
        using var response = await client.PostAsJsonAsync("/api/tickets", new
        {
            title = title ?? "Yêu cầu kiểm thử " + Guid.NewGuid().ToString("N")[..8],
            description = "Mô tả chi tiết cho yêu cầu kiểm thử tự động. Nội dung đủ dài để hợp lệ.",
            category,
            priority
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<TicketDetailResponse>();
        Assert.NotNull(detail);
        return detail;
    }

    private async Task<TicketDetailResponse> AssignAsync(
        HttpClient admin, TicketDetailResponse ticket, string teamCode, string agentEmail)
    {
        var teams = await admin.GetFromJsonAsync<List<TeamListResponse>>("/api/teams?includeInactive=true");
        var team = Assert.Single(teams!, x => x.Code == teamCode);

        using var agentClient = await factory.CreateClientForAsync(agentEmail);
        var agent = await agentClient.GetFromJsonAsync<UserResponse>("/api/auth/me");

        using var response = await admin.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new
        {
            teamId = team.Id,
            assignedUserId = agent!.Id,
            note = "Phân công trong kiểm thử tích hợp.",
            rowVersion = ticket.RowVersion
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<TicketDetailResponse>();
        Assert.NotNull(detail);
        return detail;
    }
}
