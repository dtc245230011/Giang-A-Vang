using SupportTicket.Contracts;

namespace SupportTicket.Infrastructure.Rules;

/// <summary>Chỉ cho phép tiến theo luồng New → Assigned → InProgress → Resolved.</summary>
public static class StatusTransitionPolicy
{
    public static bool IsValid(TicketStatus from, TicketStatus to) => (from, to) switch
    {
        (TicketStatus.New, TicketStatus.Assigned) => true,
        (TicketStatus.Assigned, TicketStatus.InProgress) => true,
        (TicketStatus.InProgress, TicketStatus.Resolved) => true,
        _ => false
    };

    public static TicketStatus? Next(TicketStatus current) => current switch
    {
        TicketStatus.New => TicketStatus.Assigned,
        TicketStatus.Assigned => TicketStatus.InProgress,
        TicketStatus.InProgress => TicketStatus.Resolved,
        _ => null
    };
}
