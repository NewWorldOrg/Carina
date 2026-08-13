using Carina.Api.Responder;
using Carina.Api.Responder.DriverStatus;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.DriverStatus;

[ApiController]
[Route("api/driver/status")]
public sealed class GetDriverStatusAction(DriverStatusService driverStatusService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<DriverStatusResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        var result = await driverStatusService.GetStatusAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                BaseResponder<DriverStatusResponder>.Error(result.ErrorMessage ?? string.Empty));
        }

        var snapshot = result.Data!;
        return Ok(BaseResponder<DriverStatusResponder>.Success(
            new DriverStatusResponder(snapshot.Connection, snapshot.ObservedAt)));
    }
}
