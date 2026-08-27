using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Reservations;
using Carina.Api.Services;
using Carina.Domain.Reservations;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Reservations;

[ApiController]
[Route("api/reservations/{id:guid}/restore")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class RestoreReservationAction(ReservationService reservations) : ControllerBase
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

        ServiceResult<ReservationSettlement, ReservationFailure> restored = await reservations.ReviseAsync(
            reservationId,
            new ReservationRevision { Move = ReservationMove.Restore },
            cancellationToken);

        return restored.IsSuccess
            ? Ok(BaseResponder<ReservationSettlementResponder>.Success(
                ReservationSettlementResponder.Of(restored.Data!)))
            : StatusCode(
                ReservationStatus.Of(restored.ErrorType),
                BaseResponder<ReservationSettlementResponder>.Error(restored.ErrorMessage!));
    }
}
