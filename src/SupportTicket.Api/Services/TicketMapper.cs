using SupportTicket.Api.Dtos;
using SupportTicket.Contracts;
using SupportTicket.Infrastructure.Entities;
using SupportTicket.Infrastructure.Security;

namespace SupportTicket.Api.Services;

public static class TicketMapper
{
    public static TicketListItemDto ToListItem(Ticket ticket) => new(
        ticket.Id,
        ticket.TicketNumber,
        ticket.Title,
        ticket.Category,
        ticket.Priority,
        ticket.Status,
        new PersonRefDto(ticket.Requester.Id, ticket.Requester.FullName, ticket.Requester.Email),
        ticket.AssignedTeam is null ? null : new TeamRefDto(ticket.AssignedTeam.Id, ticket.AssignedTeam.Code, ticket.AssignedTeam.Name),
        ticket.AssignedUser is null ? null : new PersonRefDto(ticket.AssignedUser.Id, ticket.AssignedUser.FullName, ticket.AssignedUser.Email),
        ticket.CreatedAt,
        ticket.UpdatedAt,
        ticket.ResolvedAt);

    public static TicketDetailDto ToDetail(Ticket ticket, UserContext user, bool canManage)
    {
        var nextStatuses = new List<TicketStatus>();
        if (canManage)
        {
            if (ticket.Status == TicketStatus.Assigned)
            {
                nextStatuses.Add(TicketStatus.InProgress);
            }
            else if (ticket.Status == TicketStatus.InProgress)
            {
                nextStatuses.Add(TicketStatus.Resolved);
            }
        }

        return new TicketDetailDto(
            ticket.Id,
            ticket.TicketNumber,
            ticket.Title,
            ticket.Description,
            ticket.Category,
            ticket.Priority,
            ticket.Status,
            new PersonRefDto(ticket.Requester.Id, ticket.Requester.FullName, ticket.Requester.Email),
            ticket.AssignedTeam is null ? null : new TeamRefDto(ticket.AssignedTeam.Id, ticket.AssignedTeam.Code, ticket.AssignedTeam.Name),
            ticket.AssignedUser is null ? null : new PersonRefDto(ticket.AssignedUser.Id, ticket.AssignedUser.FullName, ticket.AssignedUser.Email),
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.ResolvedAt,
            Convert.ToBase64String(ticket.RowVersion),
            nextStatuses.Count > 0,
            user.IsAdmin && ticket.Status != TicketStatus.Resolved,
            nextStatuses);
    }
}
