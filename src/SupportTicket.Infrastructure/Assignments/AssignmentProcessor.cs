using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SupportTicket.Contracts;
using SupportTicket.Infrastructure.Data;
using SupportTicket.Infrastructure.Entities;
using SupportTicket.Infrastructure.Messaging;
using SupportTicket.Infrastructure.Rules;

namespace SupportTicket.Infrastructure.Assignments;

public enum AssignmentOutcome
{
    Assigned,
    AlreadyAssigned,
    Duplicate,
    TicketNotFound
}

/// <summary>
/// Áp dụng quy tắc phân công cho một sự kiện TicketCreated.
/// Ticket, lịch sử, inbox và sự kiện TicketAssigned được ghi trong cùng một transaction.
/// </summary>
public sealed class AssignmentProcessor(AppDbContext db, ILogger<AssignmentProcessor> logger)
{
    public async Task<AssignmentOutcome> ProcessAsync(TicketCreatedEvent message, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            if (await db.InboxMessages.AnyAsync(x => x.MessageId == message.MessageId, cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Bỏ qua message trùng {MessageId}.", message.MessageId);
                return AssignmentOutcome.Duplicate;
            }

            var ticket = await db.Tickets.FirstOrDefaultAsync(x => x.Id == message.TicketId, cancellationToken);
            if (ticket is null)
            {
                db.InboxMessages.Add(NewInbox(message.MessageId));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                logger.LogWarning("Không tìm thấy ticket {TicketId} cho message {MessageId}.", message.TicketId, message.MessageId);
                return AssignmentOutcome.TicketNotFound;
            }

            if (ticket.Status != TicketStatus.New || ticket.AssignedTeamId is not null || ticket.AssignedUserId is not null)
            {
                db.InboxMessages.Add(NewInbox(message.MessageId));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation(
                    "Ticket {TicketNumber} đã được phân công trước đó; không ghi đè (message {MessageId}).",
                    ticket.TicketNumber, message.MessageId);
                return AssignmentOutcome.AlreadyAssigned;
            }

            var teamCode = AssignmentRules.ResolveTeamCode(ticket.Category);
            var team = await db.Teams
                .Include(x => x.Members)
                .ThenInclude(x => x.User)
                .FirstOrDefaultAsync(x => x.Code == teamCode && x.IsActive, cancellationToken);

            if (team is null)
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy nhóm xử lý '{teamCode}' cho ticket {ticket.TicketNumber}.");
            }

            var agent = team.Members
                .Where(x => x.User.IsActive && x.User.Role == UserRole.Agent)
                .OrderByDescending(x => x.IsLead)
                .ThenBy(x => x.JoinedAt)
                .Select(x => x.User)
                .FirstOrDefault();

            var now = DateTime.UtcNow;
            var oldStatus = ticket.Status;

            ticket.AssignedTeamId = team.Id;
            ticket.AssignedUserId = agent?.Id;
            ticket.Status = TicketStatus.Assigned;
            ticket.UpdatedAt = now;

            var note = agent is null
                ? "Hệ thống tự động giao nhóm; chưa có nhân viên phù hợp nên chưa gán người xử lý."
                : "Hệ thống tự động phân công theo danh mục.";

            db.TicketHistories.Add(new TicketHistory
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                ChangedByUserId = null,
                FieldChanged = "AssignedTeamId",
                OldValue = null,
                NewValue = team.Code,
                Note = note,
                ChangedAt = now
            });

            if (agent is not null)
            {
                db.TicketHistories.Add(new TicketHistory
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticket.Id,
                    ChangedByUserId = null,
                    FieldChanged = "AssignedUserId",
                    OldValue = null,
                    NewValue = agent.FullName,
                    Note = note,
                    ChangedAt = now
                });
            }

            db.TicketHistories.Add(new TicketHistory
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                ChangedByUserId = null,
                FieldChanged = "Status",
                OldValue = oldStatus.ToString(),
                NewValue = TicketStatus.Assigned.ToString(),
                Note = note,
                ChangedAt = now
            });

            var eventId = Guid.NewGuid();
            var assignedEvent = new TicketAssignedEvent(
                eventId,
                ticket.Id,
                ticket.TicketNumber,
                ticket.Title,
                team.Id,
                team.Code,
                team.Name,
                agent?.Id,
                agent?.FullName,
                oldStatus,
                TicketStatus.Assigned,
                ChangedByUserId: null,
                now);

            db.OutboxMessages.Add(OutboxFactory.Create(
                assignedEvent, eventId, EventTypes.TicketAssigned, RoutingKeys.TicketAssigned, now));

            db.InboxMessages.Add(NewInbox(message.MessageId));

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Đã phân công ticket {TicketNumber} cho nhóm {TeamCode} và nhân viên {AgentId}.",
                ticket.TicketNumber, team.Code, agent?.Id);

            return AssignmentOutcome.Assigned;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static InboxMessage NewInbox(Guid messageId) => new()
    {
        MessageId = messageId,
        EventType = EventTypes.TicketCreated,
        ProcessedAt = DateTime.UtcNow
    };
}
