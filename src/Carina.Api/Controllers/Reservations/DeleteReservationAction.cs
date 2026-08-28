using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Reservations;
using Carina.Api.Services;
using Carina.Domain.Reservations;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Reservations;

[ApiController]
[Route("api/reservations/{id:guid}")]
[EndpointEffect(EndpointEffect.Destructive)]
public sealed class DeleteReservationAction(ReservationService reservations) : ControllerBase
{
    [HttpDelete]
    [ProducesResponseType<BaseResponder<ReservationDiscardResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<ReservationDiscardResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<ReservationDiscardRefusedResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<ReservationDiscardRefusedResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(Guid id, CancellationToken cancellationToken)
    {
        if (ReservationIdText.Read(id) is not { } reservationId)
        {
            return BadRequest(BaseResponder<ReservationDiscardResponder>.Error(ReservationIdText.Description));
        }

        ServiceResult<ReservationDiscarded, ReservationFailure> discarded =
            await reservations.DiscardAsync(reservationId, cancellationToken);

        return discarded.IsSuccess
            ? Ok(BaseResponder<ReservationDiscardResponder>.Success(
                ReservationDiscardResponder.Of(discarded.Data!)))
            : StatusCode(
                ReservationStatus.Of(discarded.ErrorType),
                new BaseResponder<ReservationDiscardRefusedResponder>(
                    false,
                    discarded.ErrorMessage!,
                    ReservationDiscardRefusedResponder.Of(id, discarded.ErrorType)));
    }
}
