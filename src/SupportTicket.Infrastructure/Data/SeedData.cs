using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SupportTicket.Contracts;
using SupportTicket.Infrastructure.Entities;

namespace SupportTicket.Infrastructure.Data;

/// <summary>
/// Seed dữ liệu chạy lại được nhiều lần mà không nhân bản bản ghi.
/// Khóa nghiệp vụ để kiểm tra tồn tại là email, mã nhóm và mã ticket.
/// </summary>
public static class SeedData
{
    private static readonly Guid TechTeamId = Guid.Parse("11111111-1111-1111-1111-111111111101");
    private static readonly Guid AccountTeamId = Guid.Parse("11111111-1111-1111-1111-111111111102");
    private static readonly Guid CskhTeamId = Guid.Parse("11111111-1111-1111-1111-111111111103");
    private static readonly Guid GeneralTeamId = Guid.Parse("11111111-1111-1111-1111-111111111104");

    private static readonly Guid AdminId = Guid.Parse("22222222-2222-2222-2222-222222222201");
    private static readonly Guid TechAgentId = Guid.Parse("22222222-2222-2222-2222-222222222301");
    private static readonly Guid AccountAgentId = Guid.Parse("22222222-2222-2222-2222-222222222302");
    private static readonly Guid CskhAgentId = Guid.Parse("22222222-2222-2222-2222-222222222303");
    private static readonly Guid GeneralAgentId = Guid.Parse("22222222-2222-2222-2222-222222222304");
    private static readonly Guid CustomerId = Guid.Parse("22222222-2222-2222-2222-222222222401");
    private static readonly Guid Customer2Id = Guid.Parse("22222222-2222-2222-2222-222222222402");

    public static async Task SeedAsync(
        AppDbContext db,
        SeedOptions options,
        IPasswordHasher<User> hasher,
        IHostEnvironment environment,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var password = ResolvePassword(options, environment, logger);

        var teams = new[]
        {
            new Team { Id = TechTeamId, Code = "TECH", Name = "Kỹ thuật", Description = "Xử lý tài khoản, đăng nhập và lỗi kỹ thuật", CreatedAt = DateTime.UtcNow },
            new Team { Id = AccountTeamId, Code = "ACCT", Name = "Kế toán", Description = "Xử lý thanh toán, hóa đơn, hoàn tiền", CreatedAt = DateTime.UtcNow },
            new Team { Id = CskhTeamId, Code = "CSKH", Name = "Chăm sóc khách hàng", Description = "Xử lý giao hàng, đổi trả và khiếu nại", CreatedAt = DateTime.UtcNow },
            new Team { Id = GeneralTeamId, Code = "GENERAL", Name = "Hỗ trợ chung", Description = "Tiếp nhận các yêu cầu khác", CreatedAt = DateTime.UtcNow }
        };

        var existingTeamCodes = await db.Teams.Select(x => x.Code).ToListAsync(cancellationToken);
        foreach (var team in teams.Where(x => !existingTeamCodes.Contains(x.Code)))
        {
            db.Teams.Add(team);
        }

        await db.SaveChangesAsync(cancellationToken);

        var teamByCode = await db.Teams.ToDictionaryAsync(x => x.Code, cancellationToken);

        var users = new[]
        {
            NewUser(AdminId, "Quản trị viên", "admin@support.local", UserRole.Admin, password, hasher),
            NewUser(TechAgentId, "Nguyễn Văn Kỹ Thuật", "agent.tech@support.local", UserRole.Agent, password, hasher),
            NewUser(AccountAgentId, "Phạm Thị Kế Toán", "agent.acct@support.local", UserRole.Agent, password, hasher),
            NewUser(CskhAgentId, "Lê Thị Chăm Sóc", "agent.cskh@support.local", UserRole.Agent, password, hasher),
            NewUser(GeneralAgentId, "Hoàng Văn Hỗ Trợ", "agent.general@support.local", UserRole.Agent, password, hasher),
            NewUser(CustomerId, "Nguyễn Thị An", "customer@support.local", UserRole.Customer, password, hasher),
            NewUser(Customer2Id, "Trần Thị Bình", "customer2@support.local", UserRole.Customer, password, hasher)
        };

        var existingEmails = await db.Users.Select(x => x.Email).ToListAsync(cancellationToken);
        foreach (var user in users.Where(x => !existingEmails.Contains(x.Email)))
        {
            db.Users.Add(user);
        }

        await db.SaveChangesAsync(cancellationToken);

        var memberships = new (Guid TeamId, Guid UserId, bool IsLead)[]
        {
            (TechTeamId, TechAgentId, true),
            (AccountTeamId, AccountAgentId, true),
            (CskhTeamId, CskhAgentId, true),
            (GeneralTeamId, GeneralAgentId, true)
        };

        var existingMemberships = await db.TeamMembers
            .Select(x => new { x.TeamId, x.UserId })
            .ToListAsync(cancellationToken);

        foreach (var membership in memberships)
        {
            if (!existingMemberships.Any(x => x.TeamId == membership.TeamId && x.UserId == membership.UserId))
            {
                db.TeamMembers.Add(new TeamMember
                {
                    TeamId = membership.TeamId,
                    UserId = membership.UserId,
                    IsLead = membership.IsLead,
                    JoinedAt = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (options.IncludeDemoTickets)
        {
            await SeedDemoTicketsAsync(db, teamByCode, cancellationToken);
        }

        logger.LogInformation("Seed dữ liệu hoàn tất: {TeamCount} nhóm, {UserCount} tài khoản.", teamByCode.Count, users.Length);
    }

    private static string ResolvePassword(SeedOptions options, IHostEnvironment environment, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(options.DefaultPassword))
        {
            if (options.DefaultPassword.Length < 8)
            {
                throw new InvalidOperationException("Seed:DefaultPassword phải có ít nhất 8 ký tự.");
            }

            return options.DefaultPassword;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Chưa cấu hình Seed:DefaultPassword. Hãy đặt biến môi trường Seed__DefaultPassword trước khi chạy.");
        }

        var generated = "Demo@" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
        logger.LogWarning(
            "Chưa cấu hình Seed:DefaultPassword. Đã sinh mật khẩu tạm cho tài khoản demo: {Password} — chỉ dùng cho môi trường phát triển.",
            generated);
        return generated;
    }

    private static User NewUser(Guid id, string fullName, string email, UserRole role, string password, IPasswordHasher<User> hasher)
    {
        var user = new User
        {
            Id = id,
            FullName = fullName,
            Email = email,
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        return user;
    }

    private static async Task SeedDemoTicketsAsync(
        AppDbContext db,
        IReadOnlyDictionary<string, Team> teams,
        CancellationToken cancellationToken)
    {
        var existingNumbers = await db.Tickets.Select(x => x.TicketNumber).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var demoTickets = new[]
        {
            NewDemoTicket(
                Guid.Parse("33333333-3333-3333-3333-333333333301"), "TK-DEMO-001", CustomerId,
                "Không đăng nhập được vào tài khoản",
                "Tôi đã đổi mật khẩu hôm qua nhưng hôm nay không thể đăng nhập. Hệ thống báo sai mật khẩu dù tôi chắc chắn nhập đúng. Mong được hỗ trợ sớm.",
                TicketCategory.Account, TicketPriority.High, TicketStatus.New, null, null,
                now.AddHours(-2), now.AddHours(-2), null),
            NewDemoTicket(
                Guid.Parse("33333333-3333-3333-3333-333333333302"), "TK-DEMO-002", CustomerId,
                "Hóa đơn tháng 11 bị tính trùng",
                "Tôi nhận được hai hóa đơn cho cùng một đơn hàng trong tháng 11. Nhờ kế toán kiểm tra và hoàn lại khoản chênh lệch.",
                TicketCategory.Payment, TicketPriority.Medium, TicketStatus.Assigned, AccountTeamId, AccountAgentId,
                now.AddDays(-1), now.AddHours(-20), null),
            NewDemoTicket(
                Guid.Parse("33333333-3333-3333-3333-333333333303"), "TK-DEMO-003", CustomerId,
                "Đơn hàng giao chậm hơn dự kiến",
                "Đơn hàng của tôi dự kiến giao cách đây ba ngày nhưng vẫn đang ở trạng thái vận chuyển. Nhờ kiểm tra giúp.",
                TicketCategory.Delivery, TicketPriority.High, TicketStatus.InProgress, CskhTeamId, CskhAgentId,
                now.AddDays(-2), now.AddDays(-1), null),
            NewDemoTicket(
                Guid.Parse("33333333-3333-3333-3333-333333333304"), "TK-DEMO-004", CustomerId,
                "Yêu cầu xuất hóa đơn điện tử",
                "Tôi cần hóa đơn điện tử cho đơn hàng đã mua để gửi cho công ty. Vui lòng hướng dẫn thủ tục.",
                TicketCategory.Other, TicketPriority.Low, TicketStatus.Resolved, GeneralTeamId, GeneralAgentId,
                now.AddDays(-4), now.AddDays(-3), now.AddDays(-3)),
            NewDemoTicket(
                Guid.Parse("33333333-3333-3333-3333-333333333305"), "TK-DEMO-005", CustomerId,
                "Tài khoản bị khóa sau nhiều lần đăng nhập sai",
                "Tài khoản của tôi bị tạm khóa sau khi tôi nhập sai mật khẩu vài lần. Nhờ mở khóa giúp.",
                TicketCategory.Account, TicketPriority.Medium, TicketStatus.Resolved, TechTeamId, TechAgentId,
                now.AddDays(-6), now.AddDays(-5), now.AddDays(-5)),
            NewDemoTicket(
                Guid.Parse("33333333-3333-3333-3333-333333333306"), "TK-DEMO-006", Customer2Id,
                "Cần hỗ trợ phương thức thanh toán mới",
                "Tôi muốn biết cửa hàng có hỗ trợ thanh toán bằng ví điện tử hay không và cần chuẩn bị những gì.",
                TicketCategory.Payment, TicketPriority.Low, TicketStatus.New, null, null,
                now.AddHours(-3), now.AddHours(-3), null)
        };

        foreach (var ticket in demoTickets.Where(x => !existingNumbers.Contains(x.TicketNumber)))
        {
            db.Tickets.Add(ticket);
        }

        await db.SaveChangesAsync(cancellationToken);

        // Bổ sung lịch sử cho ticket demo còn thiếu lịch sử.
        var demoNumbers = demoTickets.Select(x => x.TicketNumber).ToList();
        var ticketsWithHistory = await db.Tickets
            .Where(x => demoNumbers.Contains(x.TicketNumber))
            .Where(x => x.Histories.Count > 0)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var ticket in demoTickets.Where(x => !ticketsWithHistory.Contains(x.Id)))
        {
            db.TicketHistories.Add(new TicketHistory
            {
                Id = Guid.NewGuid(), TicketId = ticket.Id, ChangedByUserId = ticket.RequesterId,
                FieldChanged = "Status", OldValue = null, NewValue = nameof(TicketStatus.New),
                Note = "Khách hàng tạo yêu cầu", ChangedAt = ticket.CreatedAt
            });

            if (ticket.AssignedTeamId is { } teamId)
            {
                var team = teams.Values.First(x => x.Id == teamId);
                db.TicketHistories.Add(new TicketHistory
                {
                    Id = Guid.NewGuid(), TicketId = ticket.Id, ChangedByUserId = null,
                    FieldChanged = "AssignedTeamId", OldValue = null, NewValue = team.Code,
                    Note = "Hệ thống tự động phân công", ChangedAt = ticket.CreatedAt.AddMinutes(1)
                });
                db.TicketHistories.Add(new TicketHistory
                {
                    Id = Guid.NewGuid(), TicketId = ticket.Id, ChangedByUserId = null,
                    FieldChanged = "Status", OldValue = nameof(TicketStatus.New), NewValue = nameof(TicketStatus.Assigned),
                    Note = "Hệ thống tự động phân công", ChangedAt = ticket.CreatedAt.AddMinutes(1)
                });
            }

            if ((ticket.Status is TicketStatus.InProgress or TicketStatus.Resolved) && ticket.AssignedTeamId is not null)
            {
                db.TicketHistories.Add(new TicketHistory
                {
                    Id = Guid.NewGuid(), TicketId = ticket.Id, ChangedByUserId = ticket.AssignedUserId,
                    FieldChanged = "Status", OldValue = nameof(TicketStatus.Assigned), NewValue = nameof(TicketStatus.InProgress),
                    Note = "Nhân viên bắt đầu xử lý", ChangedAt = ticket.UpdatedAt.AddHours(-4)
                });
            }

            if (ticket.Status == TicketStatus.Resolved)
            {
                db.TicketHistories.Add(new TicketHistory
                {
                    Id = Guid.NewGuid(), TicketId = ticket.Id, ChangedByUserId = ticket.AssignedUserId,
                    FieldChanged = "Status", OldValue = nameof(TicketStatus.InProgress), NewValue = nameof(TicketStatus.Resolved),
                    Note = "Đã giải quyết xong", ChangedAt = ticket.ResolvedAt ?? ticket.UpdatedAt
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Ticket NewDemoTicket(
        Guid id, string number, Guid requesterId, string title, string description,
        TicketCategory category, TicketPriority priority, TicketStatus status,
        Guid? teamId, Guid? userId, DateTime createdAt, DateTime updatedAt, DateTime? resolvedAt)
        => new()
        {
            Id = id,
            TicketNumber = number,
            RequesterId = requesterId,
            Title = title,
            Description = description,
            Category = category,
            Priority = priority,
            Status = status,
            AssignedTeamId = teamId,
            AssignedUserId = userId,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            ResolvedAt = resolvedAt
        };
}
