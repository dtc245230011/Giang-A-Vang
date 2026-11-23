using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupportTicket.Api.Dtos;
using SupportTicket.Api.Security;
using SupportTicket.Api.Services;

namespace SupportTicket.Api.Controllers;

[ApiController]
[Authorize]
[Route("dashboard")]
[Route("api/dashboard")]
public sealed class DashboardController(TicketService ticketService) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> Summary(CancellationToken cancellationToken)
        => Ok(await ticketService.GetDashboardAsync(User.ToUserContext(), cancellationToken));
}
