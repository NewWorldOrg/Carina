using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Tuners;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Tuners;

[ApiController]
[Route("api/tuners/{deviceId}/lnb-power")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class PutTunerLnbPowerAction(TunerLedgerService tunerLedgerService) : ControllerBase
{
    [HttpPut]
    [ProducesResponseType<BaseResponder<TunerLedgerResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<TunerLedgerResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<TunerLedgerResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<TunerLedgerResponder>>(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType<BaseResponder<TunerLedgerResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(
        string deviceId,
        [FromBody] LnbPowerRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.LnbPower is not { } on)
        {
            return BadRequest(BaseResponder<TunerLedgerResponder>.Error(
                "lnbPower: expected true to power the low-noise block from this tuner or false to stop."));
        }

        ServiceResult<TunerLedgerView, TunerLedgerFailure> result =
            await tunerLedgerService.SwitchLnbPowerAsync(deviceId, on, cancellationToken);

        if (!result.IsSuccess)
        {
            return StatusCode(
                TunerLedgerStatus.Of(result.ErrorType),
                BaseResponder<TunerLedgerResponder>.Error(result.ErrorMessage!));
        }

        return Ok(BaseResponder<TunerLedgerResponder>.Success(TunerLedgerResponder.Of(result.Data!)));
    }
}
