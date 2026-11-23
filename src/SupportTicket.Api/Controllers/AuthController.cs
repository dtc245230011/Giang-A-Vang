using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
[Route("auth")]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher,
    TokenService tokenService) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Email == email, cancellationToken))
        {
            throw AppException.Conflict("Email đã được sử dụng.");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = email,
            Role = UserRole.Customer,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Hai yêu cầu đăng ký cùng email có thể vượt qua kiểm tra tồn tại; unique index là chốt cuối.
            throw AppException.Conflict("Email đã được sử dụng.");
        }

        var (token, expiresAt) = tokenService.Create(user);
        var response = new AuthResponse(token, expiresAt, ToDto(user));
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);

        if (user is null)
        {
            throw AppException.Unauthorized("Email hoặc mật khẩu không đúng.");
        }

        if (!user.IsActive)
        {
            throw AppException.Forbidden("Tài khoản đã bị vô hiệu hóa. Vui lòng liên hệ quản trị viên.");
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            throw AppException.Unauthorized("Email hoặc mật khẩu không đúng.");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(cancellationToken);
        }

        var (token, expiresAt) = tokenService.Create(user);
        return Ok(new AuthResponse(token, expiresAt, ToDto(user)));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken)
    {
        var current = User.ToUserContext();
        var user = await db.Users.AsNoTracking()
                       .FirstOrDefaultAsync(x => x.Id == current.Id, cancellationToken)
                   ?? throw AppException.Unauthorized("Tài khoản không tồn tại.");

        return Ok(ToDto(user));
    }

    /// <summary>JWT là stateless; client xóa token. Endpoint giữ để giao diện gọi thống nhất.</summary>
    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout() => NoContent();

    private static UserDto ToDto(User user) =>
        new(user.Id, user.FullName, user.Email, user.Role, user.IsActive, user.CreatedAt);
}
