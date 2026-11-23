using SupportTicket.Contracts;

namespace SupportTicket.Infrastructure.Security;

/// <summary>Danh tính đã xác thực, được dựng từ JWT ở server.</summary>
public sealed record UserContext(Guid Id, string FullName, string Email, UserRole Role)
{
    public bool IsAdmin => Role == UserRole.Admin;
    public bool IsAgent => Role == UserRole.Agent;
}
