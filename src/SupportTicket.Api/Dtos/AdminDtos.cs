using System.ComponentModel.DataAnnotations;
using SupportTicket.Contracts;

namespace SupportTicket.Api.Dtos;

public sealed record CreateUserRequest(
    [Required(ErrorMessage = "Họ tên là bắt buộc.")]
    [StringLength(150, MinimumLength = 2)]
    string FullName,
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(256)]
    string Email,
    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải từ 8 đến 100 ký tự.")]
    string Password,
    UserRole Role,
    Guid? TeamId,
    bool IsLead = false);

public sealed record UpdateUserRequest(
    [StringLength(150, MinimumLength = 2)] string? FullName,
    UserRole? Role,
    bool? IsActive,
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Mật khẩu phải từ 8 đến 100 ký tự.")]
    string? NewPassword);

public sealed record AdminUserDto(
    Guid Id,
    string FullName,
    string Email,
    UserRole Role,
    bool IsActive,
    DateTime CreatedAt,
    IReadOnlyList<TeamRefDto> Teams);

public sealed record TeamDto(
    Guid Id, string Code, string Name, string? Description, bool IsActive, int MemberCount);

public sealed record AgentDto(
    Guid Id, string FullName, string Email, IReadOnlyList<TeamRefDto> Teams);

public sealed record TeamMemberDto(
    Guid UserId, string FullName, string Email, UserRole Role, bool IsActive, bool IsLead, DateTime JoinedAt);

public sealed record TeamDetailDto(
    Guid Id, string Code, string Name, string? Description, bool IsActive,
    IReadOnlyList<TeamMemberDto> Members);

public sealed record CreateTeamRequest(
    [Required(ErrorMessage = "Mã nhóm là bắt buộc.")]
    [StringLength(20, MinimumLength = 2, ErrorMessage = "Mã nhóm phải từ 2 đến 20 ký tự.")]
    [RegularExpression("^[A-Za-z0-9_-]+$", ErrorMessage = "Mã nhóm chỉ gồm chữ, số, gạch ngang hoặc gạch dưới.")]
    string Code,
    [Required(ErrorMessage = "Tên nhóm là bắt buộc.")]
    [StringLength(100, MinimumLength = 2)]
    string Name,
    [StringLength(500)]
    string? Description);

public sealed record UpdateTeamRequest(
    [StringLength(100, MinimumLength = 2)] string? Name,
    [StringLength(500)] string? Description,
    bool? IsActive);

public sealed record AddTeamMemberRequest(bool IsLead = false);

public sealed record MetaOptionDto(string Value, string Label);

public sealed record MetaDto(
    IReadOnlyList<MetaOptionDto> Categories,
    IReadOnlyList<MetaOptionDto> Priorities,
    IReadOnlyList<MetaOptionDto> Statuses,
    IReadOnlyList<MetaOptionDto> Roles);
