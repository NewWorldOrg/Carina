using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Reservations;
using Carina.Api.Services;
using Carina.Domain.Reservations;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Reservations;

[ApiController]
[Route("api/reservations/{id:guid}")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class ReviseReservationAction(ReservationService reservations) : ControllerBase
{
    [HttpPatch]
    [ProducesResponseType<BaseResponder<ReservationSettlementResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<ReservationSettlementResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<ReservationSettlementResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<ReservationSettlementResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<ReservationSettlementResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(
        Guid id,
        [FromBody] ReviseReservationRequest? request,
        CancellationToken cancellationToken)
    {
        if (ReservationIdText.Read(id) is not { } reservationId)
        {
            return BadRequest(BaseResponder<ReservationSettlementResponder>.Error(ReservationIdText.Description));
        }

        if (!ReservationInput.Holds(request?.Priority, request?.MarginBeforeSeconds, request?.MarginAfterSeconds))
        {
            return BadRequest(BaseResponder<ReservationSettlementResponder>.Error(ReservationInput.Description));
        }

        var revision = new ReservationRevision
        {
            Priority = ReservationInput.PriorityOf(request?.Priority),
            MarginBefore = ReservationInput.MarginOf(request?.MarginBeforeSeconds),
            MarginAfter = ReservationInput.MarginOf(request?.MarginAfterSeconds),
        };

        if (revision.ChangesNothing)
        {
            return BadRequest(BaseResponder<ReservationSettlementResponder>.Error(
                "A change names what to change: the priority, the margin before, or the margin after. "
                + ReservationInput.Description));
        }

        ServiceResult<ReservationSettlement, ReservationFailure> revised =
            await reservations.ReviseAsync(reservationId, revision, cancellationToken);

        return revised.IsSuccess
            ? Ok(BaseResponder<ReservationSettlementResponder>.Success(
                ReservationSettlementResponder.Of(revised.Data!)))
            : StatusCode(
                ReservationStatus.Of(revised.ErrorType),
                BaseResponder<ReservationSettlementResponder>.Error(revised.ErrorMessage!));
    }
}
