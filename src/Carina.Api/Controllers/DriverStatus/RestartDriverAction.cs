using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.DriverStatus;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.DriverStatus;

[ApiController]
[Route("api/driver/restart")]
public sealed class RestartDriverAction(DriverRestartService driverRestartService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<DriverRestartResponder>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<BaseResponder<DriverRestartResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<DriverRestartResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<DriverRestartResponder>>(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType<BaseResponder<DriverRestartResponder>>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<BaseResponder<DriverRestartResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<DriverRestartView, DriverRestartFailure> result = await driverRestartService.RequestAsync(cancellationToken);

        if (!result.IsSuccess)
        {
            return StatusCode(
                DriverRestartStatus.Of(result.ErrorType),
                BaseResponder<DriverRestartResponder>.Error(result.ErrorMessage!));
        }

        return Accepted(BaseResponder<DriverRestartResponder>.Success(
            DriverRestartResponder.Of(result.Data!)));
    }
}
