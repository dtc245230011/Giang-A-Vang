using System.ComponentModel.DataAnnotations;
using SupportTicket.Contracts;

namespace SupportTicket.Api.Dtos;

public sealed record RegisterRequest(
    [Required(ErrorMessage = "Họ tên là bắt buộc.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Họ tên phải từ 2 đến 150 ký tự.")]
    string FullName,
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(256)]
    string Email,
    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải từ 8 đến 100 ký tự.")]
    string Password);

public sealed record LoginRequest(
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    string Email,
    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    string Password);

public sealed record UserDto(Guid Id, string FullName, string Email, UserRole Role, bool IsActive, DateTime CreatedAt);

public sealed record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);
