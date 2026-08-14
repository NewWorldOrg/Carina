using Carina.Api.Responder;
using Carina.Api.Responder.Tuners;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Tuners;

[ApiController]
[Route("api/tuners/detected")]
public sealed class GetDetectedTunersAction(TunerLedgerService tunerLedgerService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<DetectedTunersResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<DetectedTunersResponder>>(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType<BaseResponder<DetectedTunersResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        var result = await tunerLedgerService.DetectAsync(cancellationToken);

        if (!result.IsSuccess)
        {
            return StatusCode(
                TunerLedgerStatus.Of(result.ErrorType),
                BaseResponder<DetectedTunersResponder>.Error(result.ErrorMessage!));
        }

        return Ok(BaseResponder<DetectedTunersResponder>.Success(
            DetectedTunersResponder.Of(result.Data!)));
    }
}
