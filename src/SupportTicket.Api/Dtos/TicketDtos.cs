using System.ComponentModel.DataAnnotations;
using SupportTicket.Contracts;

namespace SupportTicket.Api.Dtos;

public sealed record CreateTicketRequest(
    [Required(ErrorMessage = "Tiêu đề là bắt buộc.")]
    [StringLength(200, MinimumLength = 5, ErrorMessage = "Tiêu đề phải từ 5 đến 200 ký tự.")]
    string Title,
    [Required(ErrorMessage = "Nội dung là bắt buộc.")]
    [StringLength(4000, MinimumLength = 10, ErrorMessage = "Nội dung phải từ 10 đến 4000 ký tự.")]
    string Description,
    [Required(ErrorMessage = "Danh mục là bắt buộc.")]
    TicketCategory Category,
    TicketPriority? Priority);

public sealed record ChangeStatusRequest(
    [Required(ErrorMessage = "Trạng thái mới là bắt buộc.")]
    TicketStatus Status,
    [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    string? Note,
    string? RowVersion);

public sealed record AssignTicketRequest(
    [Required(ErrorMessage = "Nhóm xử lý là bắt buộc.")]
    Guid TeamId,
    Guid? AssignedUserId,
    [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự.")]
    string? Note,
    string? RowVersion);

public sealed class TicketFilterQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public TicketStatus? Status { get; set; }
    public TicketPriority? Priority { get; set; }
    public TicketCategory? Category { get; set; }
    public string? Search { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public bool? Unassigned { get; set; }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalItems, int TotalPages);

public sealed record PersonRefDto(Guid Id, string FullName, string Email);

public sealed record TeamRefDto(Guid Id, string Code, string Name);

public sealed record TicketListItemDto(
    Guid Id,
    string TicketNumber,
    string Title,
    TicketCategory Category,
    TicketPriority Priority,
    TicketStatus Status,
    PersonRefDto Requester,
    TeamRefDto? AssignedTeam,
    PersonRefDto? AssignedUser,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ResolvedAt);

public sealed record TicketDetailDto(
    Guid Id,
    string TicketNumber,
    string Title,
    string Description,
    TicketCategory Category,
    TicketPriority Priority,
    TicketStatus Status,
    PersonRefDto Requester,
    TeamRefDto? AssignedTeam,
    PersonRefDto? AssignedUser,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ResolvedAt,
    string RowVersion,
    bool CanChangeStatus,
    bool CanAssign,
    IReadOnlyList<TicketStatus> AllowedNextStatuses);

public sealed record TicketHistoryDto(
    Guid Id,
    string FieldChanged,
    string? OldValue,
    string? NewValue,
    string? Note,
    DateTime ChangedAt,
    Guid? ChangedByUserId,
    string? ChangedByName);

public sealed record CountByKeyDto(string Key, int Count);

public sealed record DashboardSummaryDto(
    int Total,
    int New,
    int Assigned,
    int InProgress,
    int Resolved,
    int Unassigned,
    int ResolvedToday,
    IReadOnlyList<CountByKeyDto> ByPriority,
    IReadOnlyList<CountByKeyDto> ByCategory,
    IReadOnlyList<TicketListItemDto> RecentTickets);
