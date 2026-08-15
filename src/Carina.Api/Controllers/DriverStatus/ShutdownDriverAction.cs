using Carina.Api.Responder;
using Carina.Api.Responder.DriverStatus;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.DriverStatus;

[ApiController]
[Route("api/driver/shutdown")]
public sealed class ShutdownDriverAction(DriverShutdownService driverShutdownService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<DriverShutdownResponder>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<BaseResponder<DriverShutdownResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<DriverShutdownResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<DriverShutdownResponder>>(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType<BaseResponder<DriverShutdownResponder>>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<BaseResponder<DriverShutdownResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        var result = await driverShutdownService.RequestAsync(cancellationToken);

        if (!result.IsSuccess)
        {
            return StatusCode(
                DriverShutdownStatus.Of(result.ErrorType),
                BaseResponder<DriverShutdownResponder>.Error(result.ErrorMessage!));
        }

        return Accepted(BaseResponder<DriverShutdownResponder>.Success(
            DriverShutdownResponder.Of(result.Data!)));
    }
}
