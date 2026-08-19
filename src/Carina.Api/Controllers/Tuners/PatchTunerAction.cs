using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Tuners;
using Carina.Api.Services;
using Carina.Contracts;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Tuners;

[ApiController]
[Route("api/tuners/{deviceId}")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class PatchTunerAction(TunerLedgerService tunerLedgerService) : ControllerBase
{
    [HttpPatch]
    [ProducesResponseType<BaseResponder<TunerObservationResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<TunerObservationResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<TunerObservationResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<TunerObservationResponder>>(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType<BaseResponder<TunerObservationResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(
        string deviceId,
        [FromBody] ToggleTunerRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.Disabled is not { } disabled)
        {
            return BadRequest(BaseResponder<TunerObservationResponder>.Error(
                "disabled: expected true to take a tuner out of service or false to put it back."));
        }

        ServiceResult<TunerSnapshot, TunerLedgerFailure> result = await tunerLedgerService.ToggleAsync(deviceId, disabled, cancellationToken);

        if (!result.IsSuccess)
        {
            return StatusCode(
                TunerLedgerStatus.Of(result.ErrorType),
                BaseResponder<TunerObservationResponder>.Error(result.ErrorMessage!));
        }

        return Ok(BaseResponder<TunerObservationResponder>.Success(
            TunerObservationResponder.Of(result.Data!)));
    }
}
