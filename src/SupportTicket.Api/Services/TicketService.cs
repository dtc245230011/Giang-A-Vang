using Microsoft.EntityFrameworkCore;
using SupportTicket.Api.Dtos;
using SupportTicket.Contracts;
using SupportTicket.Infrastructure.Data;
using SupportTicket.Infrastructure.Entities;
using SupportTicket.Infrastructure.Errors;
using SupportTicket.Infrastructure.Messaging;
using SupportTicket.Infrastructure.Rules;
using SupportTicket.Infrastructure.Security;

namespace SupportTicket.Api.Services;

public sealed class TicketService(AppDbContext db)
{
    public async Task<TicketDetailDto> CreateAsync(
        CreateTicketRequest request, UserContext user, CancellationToken cancellationToken)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;

        if (title.Length is < 5 or > 200)
        {
            throw AppException.BadRequest("Tiêu đề phải từ 5 đến 200 ký tự.");
        }

        if (description.Length is < 10 or > 4000)
        {
            throw AppException.BadRequest("Nội dung phải từ 10 đến 4000 ký tự.");
        }

        var now = DateTime.UtcNow;
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            TicketNumber = await GenerateTicketNumberAsync(now, cancellationToken),
            RequesterId = user.Id,
            Title = title,
            Description = description,
            Category = request.Category,
            Priority = request.Priority ?? TicketPriority.Medium,
            Status = TicketStatus.New,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Tickets.Add(ticket);
        db.TicketHistories.Add(new TicketHistory
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            ChangedByUserId = user.Id,
            FieldChanged = "Status",
            OldValue = null,
            NewValue = TicketStatus.New.ToString(),
            Note = "Khách hàng tạo yêu cầu",
            ChangedAt = now
        });

        var eventId = Guid.NewGuid();
        var createdEvent = new TicketCreatedEvent(
            eventId, ticket.Id, ticket.TicketNumber, ticket.Category, ticket.Priority, user.Id, now);
        db.OutboxMessages.Add(OutboxFactory.Create(
            createdEvent, eventId, EventTypes.TicketCreated, RoutingKeys.TicketCreated, now));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw AppException.Conflict("Không thể tạo mã yêu cầu duy nhất. Vui lòng thử lại.");
        }

        return await GetByIdAsync(ticket.Id, user, cancellationToken);
    }

    public async Task<PagedResult<TicketListItemDto>> GetListAsync(
        TicketFilterQuery filter, UserContext user, CancellationToken cancellationToken)
    {
        var teamIds = await GetTeamIdsAsync(user.Id, cancellationToken);
        var query = ApplyVisibility(db.Tickets.AsNoTracking(), user, teamIds);
        return await QueryPagedAsync(query, filter, cancellationToken);
    }

    public async Task<TicketDetailDto> GetByIdAsync(Guid id, UserContext user, CancellationToken cancellationToken)
    {
        var teamIds = await GetTeamIdsAsync(user.Id, cancellationToken);
        var ticket = await BaseTicketQuery().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                     ?? throw AppException.NotFound("Không tìm thấy yêu cầu hỗ trợ.");

        if (!CanView(ticket, user, teamIds))
        {
            throw AppException.Forbidden("Bạn không có quyền xem yêu cầu này.");
        }

        return TicketMapper.ToDetail(ticket, user, CanManage(ticket, user, teamIds));
    }

    public async Task<PagedResult<TicketListItemDto>> GetByUserAsync(
        Guid userId, TicketFilterQuery filter, UserContext user, CancellationToken cancellationToken)
    {
        if (!user.IsAdmin && user.Id != userId)
        {
            throw AppException.Forbidden("Bạn chỉ có thể xem yêu cầu do chính mình tạo.");
        }

        IQueryable<Ticket> query;
        if (user.IsAdmin)
        {
            query = db.Tickets.AsNoTracking().Where(x => x.RequesterId == userId);
        }
        else
        {
            var teamIds = await GetTeamIdsAsync(user.Id, cancellationToken);
            query = ApplyVisibility(db.Tickets.AsNoTracking(), user, teamIds)
                .Where(x => x.RequesterId == userId);
        }

        return await QueryPagedAsync(query, filter, cancellationToken);
    }

    public async Task<IReadOnlyList<TicketHistoryDto>> GetHistoryAsync(
        Guid id, UserContext user, CancellationToken cancellationToken)
    {
        var teamIds = await GetTeamIdsAsync(user.Id, cancellationToken);
        var ticket = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                     ?? throw AppException.NotFound("Không tìm thấy yêu cầu hỗ trợ.");

        if (!CanView(ticket, user, teamIds))
        {
            throw AppException.Forbidden("Bạn không có quyền xem lịch sử của yêu cầu này.");
        }

        var entries = await db.TicketHistories
            .AsNoTracking()
            .Include(x => x.ChangedByUser)
            .Where(x => x.TicketId == id)
            .ToListAsync(cancellationToken);

        // Các thay đổi trong cùng một giao dịch có thể trùng thời điểm (độ chính xác mili giây);
        // sắp xếp thêm theo thứ tự nghiệp vụ để timeline dễ đọc: nhóm → người → trạng thái.
        return entries
            .OrderBy(x => x.ChangedAt)
            .ThenBy(x => HistoryFieldOrder(x.FieldChanged))
            .Select(x => new TicketHistoryDto(
                x.Id, x.FieldChanged, x.OldValue, x.NewValue, x.Note, x.ChangedAt,
                x.ChangedByUserId, x.ChangedByUser?.FullName))
            .ToList();
    }

    private static int HistoryFieldOrder(string fieldChanged) => fieldChanged switch
    {
        "AssignedTeamId" => 0,
        "AssignedUserId" => 1,
        "Status" => 2,
        "Priority" => 3,
        "Category" => 4,
        _ => 5
    };

    public async Task<TicketDetailDto> ChangeStatusAsync(
        Guid id, ChangeStatusRequest request, UserContext user, CancellationToken cancellationToken)
    {
        if (user.Role == UserRole.Customer)
        {
            throw AppException.Forbidden("Khách hàng không được thay đổi trạng thái yêu cầu.");
        }

        var teamIds = await GetTeamIdsAsync(user.Id, cancellationToken);
        var ticket = await BaseTicketQuery().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                     ?? throw AppException.NotFound("Không tìm thấy yêu cầu hỗ trợ.");

        if (!CanView(ticket, user, teamIds))
        {
            throw AppException.Forbidden("Bạn không có quyền xem yêu cầu này.");
        }

        if (!CanManage(ticket, user, teamIds))
        {
            throw AppException.Forbidden("Bạn không phải người xử lý hoặc thành viên nhóm được giao.");
        }

        if (ticket.Status == TicketStatus.Resolved)
        {
            throw AppException.Conflict("Yêu cầu đã hoàn thành, không thể thay đổi trạng thái.");
        }

        if (ticket.Status == TicketStatus.New)
        {
            throw AppException.Conflict("Yêu cầu chưa được phân công. Hãy dùng chức năng phân công trước.");
        }

        if (request.Status == ticket.Status)
        {
            throw AppException.Conflict("Trạng thái mới trùng với trạng thái hiện tại.");
        }

        if (!StatusTransitionPolicy.IsValid(ticket.Status, request.Status))
        {
            throw AppException.Conflict(
                $"Không thể chuyển trạng thái từ {ticket.Status} sang {request.Status}.");
        }

        EnsureRowVersion(ticket, request.RowVersion);

        var oldStatus = ticket.Status;
        var now = DateTime.UtcNow;
        ticket.Status = request.Status;
        ticket.UpdatedAt = now;
        if (request.Status == TicketStatus.Resolved)
        {
            ticket.ResolvedAt = now;
        }

        var note = string.IsNullOrWhiteSpace(request.Note)
            ? $"Chuyển trạng thái sang {request.Status}"
            : request.Note.Trim();

        db.TicketHistories.Add(new TicketHistory
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            ChangedByUserId = user.Id,
            FieldChanged = "Status",
            OldValue = oldStatus.ToString(),
            NewValue = request.Status.ToString(),
            Note = note,
            ChangedAt = now
        });

        var eventId = Guid.NewGuid();
        var statusEvent = new TicketStatusChangedEvent(
            eventId, ticket.Id, ticket.TicketNumber, ticket.Title,
            oldStatus, request.Status, user.Id,
            ticket.AssignedTeamId, ticket.AssignedTeam?.Name,
            ticket.AssignedUserId, ticket.AssignedUser?.FullName,
            now);
        db.OutboxMessages.Add(OutboxFactory.Create(
            statusEvent, eventId, EventTypes.TicketStatusChanged, RoutingKeys.TicketStatusChanged, now));

        await db.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(ticket.Id, user, cancellationToken);
    }

    public async Task<TicketDetailDto> AssignAsync(
        Guid id, AssignTicketRequest request, UserContext user, CancellationToken cancellationToken)
    {
        if (!user.IsAdmin)
        {
            throw AppException.Forbidden("Chỉ quản trị viên được phân công yêu cầu.");
        }

        var ticket = await BaseTicketQuery().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                     ?? throw AppException.NotFound("Không tìm thấy yêu cầu hỗ trợ.");

        if (ticket.Status == TicketStatus.Resolved)
        {
            throw AppException.Conflict("Yêu cầu đã hoàn thành, không thể phân công lại.");
        }

        var team = await db.Teams.FirstOrDefaultAsync(x => x.Id == request.TeamId && x.IsActive, cancellationToken)
                   ?? throw AppException.BadRequest("Nhóm xử lý không tồn tại hoặc đã ngừng hoạt động.");

        User? assignedUser = null;
        if (request.AssignedUserId is { } assignedUserId)
        {
            assignedUser = await db.Users.FirstOrDefaultAsync(x => x.Id == assignedUserId, cancellationToken)
                           ?? throw AppException.BadRequest("Nhân viên được chọn không tồn tại.");

            if (!assignedUser.IsActive || assignedUser.Role != UserRole.Agent)
            {
                throw AppException.BadRequest("Người được chọn phải là nhân viên đang hoạt động.");
            }

            var isMember = await db.TeamMembers
                .AnyAsync(x => x.TeamId == team.Id && x.UserId == assignedUserId, cancellationToken);
            if (!isMember)
            {
                throw AppException.BadRequest("Nhân viên được chọn không thuộc nhóm xử lý này.");
            }
        }

        var oldStatus = ticket.Status;
        var oldTeamId = ticket.AssignedTeamId;
        var oldUserId = ticket.AssignedUserId;
        var teamChanged = oldTeamId != team.Id;
        var userChanged = oldUserId != assignedUser?.Id;
        var statusChanged = oldStatus == TicketStatus.New;

        if (!teamChanged && !userChanged && !statusChanged)
        {
            throw AppException.Conflict("Phân công không có thay đổi.");
        }

        EnsureRowVersion(ticket, request.RowVersion);

        var now = DateTime.UtcNow;
        var note = string.IsNullOrWhiteSpace(request.Note)
            ? "Quản trị viên phân công yêu cầu"
            : request.Note.Trim();

        ticket.AssignedTeamId = team.Id;
        ticket.AssignedUserId = assignedUser?.Id;
        ticket.UpdatedAt = now;
        if (statusChanged)
        {
            ticket.Status = TicketStatus.Assigned;
            ticket.ResolvedAt = null;
        }

        if (teamChanged)
        {
            db.TicketHistories.Add(new TicketHistory
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                ChangedByUserId = user.Id,
                FieldChanged = "AssignedTeamId",
                OldValue = ticket.AssignedTeam?.Code,
                NewValue = team.Code,
                Note = note,
                ChangedAt = now
            });
        }

        if (userChanged)
        {
            db.TicketHistories.Add(new TicketHistory
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                ChangedByUserId = user.Id,
                FieldChanged = "AssignedUserId",
                OldValue = ticket.AssignedUser?.FullName,
                NewValue = assignedUser?.FullName,
                Note = note,
                ChangedAt = now
            });
        }

        if (statusChanged)
        {
            db.TicketHistories.Add(new TicketHistory
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                ChangedByUserId = user.Id,
                FieldChanged = "Status",
                OldValue = oldStatus.ToString(),
                NewValue = TicketStatus.Assigned.ToString(),
                Note = note,
                ChangedAt = now
            });
        }

        var eventId = Guid.NewGuid();
        var assignedEvent = new TicketAssignedEvent(
            eventId, ticket.Id, ticket.TicketNumber, ticket.Title,
            team.Id, team.Code, team.Name, assignedUser?.Id, assignedUser?.FullName,
            oldStatus, ticket.Status, user.Id, now);
        db.OutboxMessages.Add(OutboxFactory.Create(
            assignedEvent, eventId, EventTypes.TicketAssigned, RoutingKeys.TicketAssigned, now));

        await db.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(ticket.Id, user, cancellationToken);
    }

    public async Task<DashboardSummaryDto> GetDashboardAsync(UserContext user, CancellationToken cancellationToken)
    {
        var teamIds = await GetTeamIdsAsync(user.Id, cancellationToken);
        var query = ApplyVisibility(db.Tickets.AsNoTracking(), user, teamIds);

        var total = await query.CountAsync(cancellationToken);
        var newCount = await query.CountAsync(x => x.Status == TicketStatus.New, cancellationToken);
        var assignedCount = await query.CountAsync(x => x.Status == TicketStatus.Assigned, cancellationToken);
        var inProgressCount = await query.CountAsync(x => x.Status == TicketStatus.InProgress, cancellationToken);
        var resolvedCount = await query.CountAsync(x => x.Status == TicketStatus.Resolved, cancellationToken);
        var unassigned = await query.CountAsync(
            x => x.AssignedTeamId == null && x.Status == TicketStatus.New, cancellationToken);
        var today = DateTime.UtcNow.Date;
        var resolvedToday = await query.CountAsync(
            x => x.ResolvedAt != null && x.ResolvedAt >= today, cancellationToken);

        var priorityCounts = await query
            .GroupBy(x => x.Priority)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var categoryCounts = await query
            .GroupBy(x => x.Category)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var recent = await query
            .Include(x => x.Requester)
            .Include(x => x.AssignedTeam)
            .Include(x => x.AssignedUser)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(6)
            .ToListAsync(cancellationToken);

        var byPriority = Enum.GetValues<TicketPriority>()
            .Select(value => new CountByKeyDto(
                value.ToString(),
                priorityCounts.FirstOrDefault(x => x.Key == value)?.Count ?? 0))
            .ToList();

        var byCategory = Enum.GetValues<TicketCategory>()
            .Select(value => new CountByKeyDto(
                value.ToString(),
                categoryCounts.FirstOrDefault(x => x.Key == value)?.Count ?? 0))
            .ToList();

        return new DashboardSummaryDto(
            total, newCount, assignedCount, inProgressCount, resolvedCount, unassigned, resolvedToday,
            byPriority, byCategory, recent.Select(TicketMapper.ToListItem).ToList());
    }

    private IQueryable<Ticket> BaseTicketQuery() => db.Tickets
        .Include(x => x.Requester)
        .Include(x => x.AssignedTeam)
        .Include(x => x.AssignedUser);

    private async Task<PagedResult<TicketListItemDto>> QueryPagedAsync(
        IQueryable<Ticket> query, TicketFilterQuery filter, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize <= 0 ? 20 : filter.PageSize, 1, 100);

        query = ApplyFilter(query, filter);

        var total = await query.CountAsync(cancellationToken);
        var tickets = await query
            .Include(x => x.Requester)
            .Include(x => x.AssignedTeam)
            .Include(x => x.AssignedUser)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.TicketNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        return new PagedResult<TicketListItemDto>(
            tickets.Select(TicketMapper.ToListItem).ToList(), page, pageSize, total, totalPages);
    }

    private static IQueryable<Ticket> ApplyFilter(IQueryable<Ticket> query, TicketFilterQuery filter)
    {
        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (filter.Priority is { } priority)
        {
            query = query.Where(x => x.Priority == priority);
        }

        if (filter.Category is { } category)
        {
            query = query.Where(x => x.Category == category);
        }

        if (filter.TeamId is { } teamId)
        {
            query = query.Where(x => x.AssignedTeamId == teamId);
        }

        if (filter.AssignedUserId is { } assignedUserId)
        {
            query = query.Where(x => x.AssignedUserId == assignedUserId);
        }

        if (filter.Unassigned == true)
        {
            query = query.Where(x => x.AssignedTeamId == null);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(x => x.TicketNumber.Contains(term) || x.Title.Contains(term));
        }

        return query;
    }

    private static IQueryable<Ticket> ApplyVisibility(
        IQueryable<Ticket> query, UserContext user, IReadOnlyCollection<Guid> teamIds)
    {
        if (user.IsAdmin)
        {
            return query;
        }

        var teams = teamIds.ToList();
        if (teams.Count == 0)
        {
            return query.Where(x => x.RequesterId == user.Id || x.AssignedUserId == user.Id);
        }

        return query.Where(x =>
            x.RequesterId == user.Id
            || x.AssignedUserId == user.Id
            || (x.AssignedTeamId != null && teams.Contains(x.AssignedTeamId.Value)));
    }

    private static bool CanView(Ticket ticket, UserContext user, IReadOnlyCollection<Guid> teamIds)
    {
        if (user.IsAdmin || ticket.RequesterId == user.Id || ticket.AssignedUserId == user.Id)
        {
            return true;
        }

        return ticket.AssignedTeamId is { } teamId && teamIds.Contains(teamId);
    }

    private static bool CanManage(Ticket ticket, UserContext user, IReadOnlyCollection<Guid> teamIds)
    {
        if (user.IsAdmin)
        {
            return true;
        }

        if (user.Role != UserRole.Agent)
        {
            return false;
        }

        if (ticket.AssignedUserId == user.Id)
        {
            return true;
        }

        return ticket.AssignedTeamId is { } teamId && teamIds.Contains(teamId);
    }

    private async Task<List<Guid>> GetTeamIdsAsync(Guid userId, CancellationToken cancellationToken)
        => await db.TeamMembers
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.TeamId)
            .ToListAsync(cancellationToken);

    private async Task<string> GenerateTicketNumberAsync(DateTime now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var candidate = TicketNumberGenerator.Create(now);
            if (!await db.Tickets.AnyAsync(x => x.TicketNumber == candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw AppException.Conflict("Không thể sinh mã yêu cầu duy nhất. Vui lòng thử lại.");
    }

    private static void EnsureRowVersion(Ticket ticket, string? expectedBase64)
    {
        if (string.IsNullOrWhiteSpace(expectedBase64))
        {
            return;
        }

        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(expectedBase64);
        }
        catch (FormatException)
        {
            throw AppException.BadRequest("RowVersion không hợp lệ.");
        }

        if (!ticket.RowVersion.SequenceEqual(expected))
        {
            throw AppException.Conflict("Dữ liệu đã được người khác cập nhật. Vui lòng tải lại và thử lại.");
        }
    }
}
