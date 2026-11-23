using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupportTicket.Api.Dtos;
using SupportTicket.Api.Security;
using SupportTicket.Api.Services;

namespace SupportTicket.Api.Controllers;

[ApiController]
[Authorize]
[Route("tickets")]
[Route("api/tickets")]
public sealed class TicketsController(TicketService ticketService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TicketDetailDto>> Create(
        CreateTicketRequest request, CancellationToken cancellationToken)
    {
        var result = await ticketService.CreateAsync(request, User.ToUserContext(), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<TicketListItemDto>>> List(
        [FromQuery] TicketFilterQuery filter, CancellationToken cancellationToken)
        => Ok(await ticketService.GetListAsync(filter, User.ToUserContext(), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await ticketService.GetByIdAsync(id, User.ToUserContext(), cancellationToken));

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<IReadOnlyList<TicketHistoryDto>>> History(
        Guid id, CancellationToken cancellationToken)
        => Ok(await ticketService.GetHistoryAsync(id, User.ToUserContext(), cancellationToken));

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<TicketDetailDto>> ChangeStatus(
        Guid id, ChangeStatusRequest request, CancellationToken cancellationToken)
        => Ok(await ticketService.ChangeStatusAsync(id, request, User.ToUserContext(), cancellationToken));

    [HttpPatch("{id:guid}/assignment")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TicketDetailDto>> Assign(
        Guid id, AssignTicketRequest request, CancellationToken cancellationToken)
        => Ok(await ticketService.AssignAsync(id, request, User.ToUserContext(), cancellationToken));
}
