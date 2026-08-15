using Carina.Api.Responder;
using Carina.Api.Responder.Tuners;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Tuners;

[ApiController]
[Route("api/tuners")]
public sealed class GetTunersAction(TunerLedgerService tunerLedgerService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<TunerLedgerResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<TunerLedgerResponder>>(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType<BaseResponder<TunerLedgerResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        var result = await tunerLedgerService.ReadAsync(cancellationToken);

        if (!result.IsSuccess)
        {
            return StatusCode(
                TunerLedgerStatus.Of(result.ErrorType),
                BaseResponder<TunerLedgerResponder>.Error(result.ErrorMessage!));
        }

        return Ok(BaseResponder<TunerLedgerResponder>.Success(TunerLedgerResponder.Of(result.Data!)));
    }
}
