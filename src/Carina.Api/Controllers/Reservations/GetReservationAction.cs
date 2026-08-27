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
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetReservationAction(ReservationService reservations) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<ReservationResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<ReservationResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<ReservationResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(Guid id, CancellationToken cancellationToken)
    {
        if (ReservationIdText.Read(id) is not { } reservationId)
        {
            return BadRequest(BaseResponder<ReservationResponder>.Error(ReservationIdText.Description));
        }

        ServiceResult<Reservation, ReservationFailure> found =
            await reservations.FindAsync(reservationId, cancellationToken);

        return found.IsSuccess
            ? Ok(BaseResponder<ReservationResponder>.Success(ReservationResponder.Of(found.Data!)))
            : StatusCode(
                ReservationStatus.Of(found.ErrorType),
                BaseResponder<ReservationResponder>.Error(found.ErrorMessage!));
    }
}
