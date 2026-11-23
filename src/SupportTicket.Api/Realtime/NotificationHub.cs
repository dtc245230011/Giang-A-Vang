using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SupportTicket.Api.Security;
using SupportTicket.Infrastructure.Data;

namespace SupportTicket.Api.Realtime;

/// <summary>
/// Hub chỉ đọc danh tính từ JWT. Client không có phương thức tự tham gia nhóm của người khác.
/// </summary>
[Authorize]
public sealed class NotificationHub(AppDbContext db, ILogger<NotificationHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var user = Context.User!.ToUserContext();
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{user.Id}");

        var teamIds = await db.TeamMembers.AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .Select(x => x.TeamId)
            .ToListAsync(Context.ConnectionAborted);

        foreach (var teamId in teamIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"team:{teamId}");
        }

        logger.LogInformation(
            "SignalR kết nối: user {UserId} ({Role}), connection {ConnectionId}, {TeamCount} nhóm.",
            user.Id, user.Role, Context.ConnectionId, teamIds.Count);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        logger.LogInformation("SignalR ngắt kết nối: user {UserId}, connection {ConnectionId}.", userId, Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
