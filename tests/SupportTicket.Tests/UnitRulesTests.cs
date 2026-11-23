using Microsoft.Extensions.Logging.Abstractions;
using SupportTicket.Contracts;
using SupportTicket.Infrastructure.Rules;

namespace SupportTicket.Tests;

public sealed class AssignmentRulesTests
{
    [Theory]
    [InlineData(TicketCategory.Account, "TECH")]
    [InlineData(TicketCategory.Payment, "ACCT")]
    [InlineData(TicketCategory.Delivery, "CSKH")]
    [InlineData(TicketCategory.Other, "GENERAL")]
    public void ResolveTeamCode_MapsEveryCategory(TicketCategory category, string expected)
    {
        Assert.Equal(expected, AssignmentRules.ResolveTeamCode(category));
    }

    [Fact]
    public void ResolveTeamCode_UnknownValue_FallsBackToGeneral()
    {
        Assert.Equal("GENERAL", AssignmentRules.ResolveTeamCode((TicketCategory)99));
    }
}

public sealed class StatusTransitionPolicyTests
{
    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.Assigned, true)]
    [InlineData(TicketStatus.Assigned, TicketStatus.InProgress, true)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved, true)]
    [InlineData(TicketStatus.New, TicketStatus.InProgress, false)]
    [InlineData(TicketStatus.New, TicketStatus.Resolved, false)]
    [InlineData(TicketStatus.Assigned, TicketStatus.Resolved, false)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Assigned, false)]
    [InlineData(TicketStatus.Resolved, TicketStatus.InProgress, false)]
    [InlineData(TicketStatus.Assigned, TicketStatus.Assigned, false)]
    public void IsValid_EnforcesForwardOnlyFlow(TicketStatus from, TicketStatus to, bool expected)
    {
        Assert.Equal(expected, StatusTransitionPolicy.IsValid(from, to));
    }

    [Fact]
    public void Next_ReturnsNullAfterResolved()
    {
        Assert.Null(StatusTransitionPolicy.Next(TicketStatus.Resolved));
        Assert.Equal(TicketStatus.InProgress, StatusTransitionPolicy.Next(TicketStatus.Assigned));
    }
}

public sealed class TicketNumberGeneratorTests
{
    [Fact]
    public void Create_ProducesReadableUniqueNumbers()
    {
        var now = new DateTime(2026, 11, 23, 10, 30, 0, DateTimeKind.Utc);
        var numbers = Enumerable.Range(0, 200)
            .Select(_ => TicketNumberGenerator.Create(now))
            .ToList();

        Assert.All(numbers, number => Assert.Matches(@"^TK-261123-[A-Z2-9]{4}$", number));
        Assert.True(numbers.Distinct().Count() > 180, "Mã sinh ra phải gần như không trùng.");
    }
}
