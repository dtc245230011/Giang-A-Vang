using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SupportTicket.Api.Dtos;
using SupportTicket.Api.Security;
using SupportTicket.Contracts;
using SupportTicket.Infrastructure.Data;
using SupportTicket.Infrastructure.Entities;
using SupportTicket.Infrastructure.Errors;

namespace SupportTicket.Api.Controllers;

[ApiController]
[Authorize]
[Route("teams")]
[Route("api/teams")]
public sealed class TeamsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TeamDto>>> List(
        [FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        var canSeeInactive = includeInactive && User.ToUserContext().IsAdmin;
        var query = db.Teams.AsNoTracking();
        if (!canSeeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        var teams = await query
            .OrderBy(x => x.Code)
            .Select(x => new TeamDto(
                x.Id, x.Code, x.Name, x.Description, x.IsActive,
                x.Members.Count(m => m.User.IsActive)))
            .ToListAsync(cancellationToken);

        return Ok(teams);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Agent")]
    public async Task<ActionResult<TeamDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var team = await db.Teams.AsNoTracking()
                       .Include(x => x.Members)
                       .ThenInclude(x => x.User)
                       .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                   ?? throw AppException.NotFound("Không tìm thấy nhóm.");

        return Ok(new TeamDetailDto(
            team.Id, team.Code, team.Name, team.Description, team.IsActive,
            team.Members
                .OrderByDescending(x => x.IsLead)
                .ThenBy(x => x.User.FullName)
                .Select(x => new TeamMemberDto(
                    x.UserId, x.User.FullName, x.User.Email, x.User.Role,
                    x.User.IsActive, x.IsLead, x.JoinedAt))
                .ToList()));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TeamDto>> Create(CreateTeamRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Teams.AnyAsync(x => x.Code == code, cancellationToken))
        {
            throw AppException.Conflict("Mã nhóm đã tồn tại.");
        }

        var team = new Team
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Teams.Add(team);
        await db.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created,
            new TeamDto(team.Id, team.Code, team.Name, team.Description, team.IsActive, 0));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TeamDto>> Update(
        Guid id, UpdateTeamRequest request, CancellationToken cancellationToken)
    {
        var team = await db.Teams.Include(x => x.Members)
                       .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                   ?? throw AppException.NotFound("Không tìm thấy nhóm.");

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var name = request.Name.Trim();
            if (name.Length is < 2 or > 100)
            {
                throw AppException.BadRequest("Tên nhóm phải từ 2 đến 100 ký tự.");
            }

            team.Name = name;
        }

        if (request.Description is not null)
        {
            team.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        }

        if (request.IsActive is { } isActive)
        {
            team.IsActive = isActive;
        }

        await db.SaveChangesAsync(cancellationToken);

        var memberCount = await db.TeamMembers.CountAsync(
            x => x.TeamId == id && x.User.IsActive, cancellationToken);
        return Ok(new TeamDto(team.Id, team.Code, team.Name, team.Description, team.IsActive, memberCount));
    }

    [HttpPut("{id:guid}/members/{userId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AddMember(
        Guid id, Guid userId, [FromBody] AddTeamMemberRequest request, CancellationToken cancellationToken)
    {
        var team = await db.Teams.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                   ?? throw AppException.NotFound("Không tìm thấy nhóm.");
        if (!team.IsActive)
        {
            throw AppException.BadRequest("Nhóm đã ngừng hoạt động, không thể thêm thành viên.");
        }

        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken)
                   ?? throw AppException.NotFound("Không tìm thấy người dùng.");

        if (user.Role != UserRole.Agent)
        {
            throw AppException.BadRequest("Chỉ tài khoản nhân viên mới được tham gia nhóm xử lý.");
        }

        if (!user.IsActive)
        {
            throw AppException.BadRequest("Không thể thêm tài khoản đã bị vô hiệu hóa vào nhóm.");
        }

        if (request.IsLead)
        {
            var currentLeads = await db.TeamMembers
                .Where(x => x.TeamId == id && x.UserId != userId)
                .ToListAsync(cancellationToken);
            foreach (var lead in currentLeads)
            {
                lead.IsLead = false;
            }
        }

        var membership = await db.TeamMembers
            .FirstOrDefaultAsync(x => x.TeamId == id && x.UserId == userId, cancellationToken);

        if (membership is null)
        {
            db.TeamMembers.Add(new TeamMember
            {
                TeamId = id,
                UserId = userId,
                IsLead = request.IsLead,
                JoinedAt = DateTime.UtcNow
            });
        }
        else
        {
            membership.IsLead = request.IsLead;
        }

        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var membership = await db.TeamMembers
            .FirstOrDefaultAsync(x => x.TeamId == id && x.UserId == userId, cancellationToken)
            ?? throw AppException.NotFound("Người dùng không thuộc nhóm này.");

        db.TeamMembers.Remove(membership);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
