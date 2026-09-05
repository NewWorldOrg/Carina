using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Reservations;
using Carina.Api.Services;
using Carina.Domain.Reservations;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Reservations;

[ApiController]
[Route("api/reservations/health")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetReservationHealthAction(ReservationService reservations) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<ReservationHealthResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<ReservationHealth> counted = await reservations.HealthAsync(cancellationToken);

        return Ok(BaseResponder<ReservationHealthResponder>.Success(ReservationHealthResponder.Of(counted.Data!)));
    }
}
