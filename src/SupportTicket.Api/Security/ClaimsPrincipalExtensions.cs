using System.Security.Claims;
using SupportTicket.Contracts;
using SupportTicket.Infrastructure.Errors;
using SupportTicket.Infrastructure.Security;

namespace SupportTicket.Api.Security;

public static class ClaimsPrincipalExtensions
{
    public static UserContext ToUserContext(this ClaimsPrincipal principal)
    {
        var idText = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var roleText = principal.FindFirstValue(ClaimTypes.Role);
        var name = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

        if (!Guid.TryParse(idText, out var id) || !Enum.TryParse<UserRole>(roleText, out var role))
        {
            throw AppException.Unauthorized("Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.");
        }

        return new UserContext(id, name, email, role);
    }
}
