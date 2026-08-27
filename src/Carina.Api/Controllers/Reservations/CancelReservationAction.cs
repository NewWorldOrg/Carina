using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Reservations;
using Carina.Api.Services;
using Carina.Domain.Reservations;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Reservations;

[ApiController]
[Route("api/reservations/{id:guid}/cancel")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class CancelReservationAction(ReservationService reservations) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<ReservationSettlementResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<ReservationSettlementResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<ReservationSettlementResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<ReservationSettlementResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<ReservationSettlementResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(Guid id, CancellationToken cancellationToken)
    {
        if (ReservationIdText.Read(id) is not { } reservationId)
        {
            return BadRequest(BaseResponder<ReservationSettlementResponder>.Error(ReservationIdText.Description));
        }

        ServiceResult<ReservationSettlement, ReservationFailure> cancelled = await reservations.ReviseAsync(
            reservationId,
            new ReservationRevision { Move = ReservationMove.Cancel },
            cancellationToken);

        return cancelled.IsSuccess
            ? Ok(BaseResponder<ReservationSettlementResponder>.Success(
                ReservationSettlementResponder.Of(cancelled.Data!)))
            : StatusCode(
                ReservationStatus.Of(cancelled.ErrorType),
                BaseResponder<ReservationSettlementResponder>.Error(cancelled.ErrorMessage!));
    }
}
