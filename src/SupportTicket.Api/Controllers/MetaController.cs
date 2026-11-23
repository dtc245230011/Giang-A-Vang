using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupportTicket.Api.Dtos;
using SupportTicket.Contracts;

namespace SupportTicket.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("meta")]
[Route("api/meta")]
public sealed class MetaController : ControllerBase
{
    [HttpGet]
    public ActionResult<MetaDto> Get() => Ok(new MetaDto(
        Categories: new List<MetaOptionDto>
        {
            new(nameof(TicketCategory.Account), "Tài khoản / Đăng nhập"),
            new(nameof(TicketCategory.Payment), "Thanh toán / Hóa đơn"),
            new(nameof(TicketCategory.Delivery), "Giao hàng / Đổi trả"),
            new(nameof(TicketCategory.Other), "Khác")
        },
        Priorities: new List<MetaOptionDto>
        {
            new(nameof(TicketPriority.Low), "Thấp"),
            new(nameof(TicketPriority.Medium), "Trung bình"),
            new(nameof(TicketPriority.High), "Cao")
        },
        Statuses: new List<MetaOptionDto>
        {
            new(nameof(TicketStatus.New), "Mới"),
            new(nameof(TicketStatus.Assigned), "Đã phân công"),
            new(nameof(TicketStatus.InProgress), "Đang xử lý"),
            new(nameof(TicketStatus.Resolved), "Đã giải quyết")
        },
        Roles: new List<MetaOptionDto>
        {
            new(nameof(UserRole.Customer), "Khách hàng"),
            new(nameof(UserRole.Agent), "Nhân viên"),
            new(nameof(UserRole.Admin), "Quản trị viên")
        }));
}
