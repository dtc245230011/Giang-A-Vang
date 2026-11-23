using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SupportTicket.Api.Dtos;
using SupportTicket.Api.Security;
using SupportTicket.Api.Services;
using SupportTicket.Contracts;
using SupportTicket.Infrastructure.Data;
using SupportTicket.Infrastructure.Entities;
using SupportTicket.Infrastructure.Errors;

namespace SupportTicket.Api.Controllers;

[ApiController]
[Authorize]
[Route("users")]
[Route("api/users")]
public sealed class UsersController(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher,
    TicketService ticketService) : ControllerBase
{
    /// <summary>Ticket do một người dùng tạo. Khách hàng/nhân viên chỉ xem được của chính mình.</summary>
    [HttpGet("{userId:guid}/tickets")]
    public async Task<ActionResult<PagedResult<TicketListItemDto>>> TicketsOfUser(
        Guid userId, [FromQuery] TicketFilterQuery filter, CancellationToken cancellationToken)
        => Ok(await ticketService.GetByUserAsync(userId, filter, User.ToUserContext(), cancellationToken));

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<AdminUserDto>>> List(
        [FromQuery] string? search, [FromQuery] UserRole? role, [FromQuery] bool? isActive,
        CancellationToken cancellationToken)
    {
        var query = db.Users.AsNoTracking()
            .Include(x => x.TeamMemberships)
            .ThenInclude(x => x.Team)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.FullName.Contains(term) || x.Email.Contains(term));
        }

        if (role is { } roleFilter)
        {
            query = query.Where(x => x.Role == roleFilter);
        }

        if (isActive is { } activeFilter)
        {
            query = query.Where(x => x.IsActive == activeFilter);
        }

        var users = await query.OrderBy(x => x.FullName).Take(500).ToListAsync(cancellationToken);
        return Ok(users.Select(ToAdminDto).ToList());
    }

    [HttpGet("agents")]
    [Authorize(Roles = "Admin,Agent")]
    public async Task<ActionResult<IReadOnlyList<AgentDto>>> Agents(
        [FromQuery] Guid? teamId, CancellationToken cancellationToken)
    {
        var query = db.Users.AsNoTracking()
            .Include(x => x.TeamMemberships)
            .ThenInclude(x => x.Team)
            .Where(x => x.Role == UserRole.Agent && x.IsActive);

        if (teamId is { } id)
        {
            query = query.Where(x => x.TeamMemberships.Any(m => m.TeamId == id));
        }

        var agents = await query.OrderBy(x => x.FullName).ToListAsync(cancellationToken);
        return Ok(agents.Select(x => new AgentDto(
            x.Id, x.FullName, x.Email,
            x.TeamMemberships.Select(m => new TeamRefDto(m.Team.Id, m.Team.Code, m.Team.Name)).ToList()))
            .ToList());
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AdminUserDto>> Create(
        CreateUserRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Email == email, cancellationToken))
        {
            throw AppException.Conflict("Email đã được sử dụng.");
        }

        Team? team = null;
        if (request.TeamId is { } teamId)
        {
            team = await db.Teams.FirstOrDefaultAsync(x => x.Id == teamId, cancellationToken)
                   ?? throw AppException.BadRequest("Nhóm không tồn tại.");
            if (!team.IsActive)
            {
                throw AppException.BadRequest("Nhóm đã ngừng hoạt động, không thể thêm thành viên.");
            }
        }

        if (team is not null && request.Role != UserRole.Agent)
        {
            throw AppException.BadRequest("Chỉ nhân viên mới có thể được thêm vào nhóm xử lý.");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = email,
            Role = request.Role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        db.Users.Add(user);

        if (team is not null)
        {
            if (request.IsLead)
            {
                var existingLeads = await db.TeamMembers
                    .Where(x => x.TeamId == team.Id && x.IsLead)
                    .ToListAsync(cancellationToken);
                foreach (var existingLead in existingLeads)
                {
                    existingLead.IsLead = false;
                }
            }

            db.TeamMembers.Add(new TeamMember
            {
                TeamId = team.Id,
                UserId = user.Id,
                IsLead = request.IsLead,
                JoinedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        var created = await LoadUserAsync(user.Id, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ToAdminDto(created));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AdminUserDto>> Update(
        Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var current = User.ToUserContext();
        var user = await db.Users
                       .Include(x => x.TeamMemberships)
                       .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                   ?? throw AppException.NotFound("Không tìm thấy người dùng.");

        if (user.Id == current.Id && request.IsActive == false)
        {
            throw AppException.BadRequest("Bạn không thể tự vô hiệu hóa tài khoản của mình.");
        }

        if (user.Id == current.Id && request.Role is { } requestedRole && requestedRole != UserRole.Admin)
        {
            throw AppException.BadRequest("Bạn không thể tự hạ quyền quản trị của mình.");
        }

        if (!string.IsNullOrWhiteSpace(request.FullName))
        {
            var fullName = request.FullName.Trim();
            if (fullName.Length is < 2 or > 150)
            {
                throw AppException.BadRequest("Họ tên phải từ 2 đến 150 ký tự.");
            }

            user.FullName = fullName;
        }

        if (request.Role is { } role)
        {
            user.Role = role;
        }

        if (request.IsActive is { } isActive)
        {
            user.IsActive = isActive;
        }

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        }

        if (user.Role != UserRole.Agent && user.TeamMemberships.Count > 0)
        {
            db.TeamMembers.RemoveRange(user.TeamMemberships);
        }

        await db.SaveChangesAsync(cancellationToken);
        var updated = await LoadUserAsync(user.Id, cancellationToken);
        return Ok(ToAdminDto(updated));
    }

    private async Task<User> LoadUserAsync(Guid id, CancellationToken cancellationToken)
        => await db.Users.AsNoTracking()
               .Include(x => x.TeamMemberships)
               .ThenInclude(x => x.Team)
               .FirstAsync(x => x.Id == id, cancellationToken);

    private static AdminUserDto ToAdminDto(User user) => new(
        user.Id,
        user.FullName,
        user.Email,
        user.Role,
        user.IsActive,
        user.CreatedAt,
        user.TeamMemberships
            .Where(x => x.Team is not null)
            .Select(x => new TeamRefDto(x.Team.Id, x.Team.Code, x.Team.Name))
            .ToList());
}
