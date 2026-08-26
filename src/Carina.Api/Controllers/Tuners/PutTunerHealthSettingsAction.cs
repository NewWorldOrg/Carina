using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Tuners;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Tuners;

[ApiController]
[Route("api/tuners/health/settings")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class PutTunerHealthSettingsAction(TunerHealthService tunerHealthService) : ControllerBase
{
    [HttpPut]
    [ProducesResponseType<BaseResponder<ServiceReachSettingsResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<ServiceReachSettingsResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromBody] ServiceReachSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.HoursOfSilence is not { } hoursOfSilence)
        {
            return BadRequest(BaseResponder<ServiceReachSettingsResponder>.Error(
                "hoursOfSilence: expected the number of hours a broadcast type may go without a service before it is called missing."));
        }

        ServiceResult<int, TunerHealthFailure> result =
            await tunerHealthService.AllowSilenceForAsync(hoursOfSilence, cancellationToken);

        if (!result.IsSuccess)
        {
            return StatusCode(
                TunerHealthStatus.Of(result.ErrorType),
                BaseResponder<ServiceReachSettingsResponder>.Error(result.ErrorMessage!));
        }

        return Ok(BaseResponder<ServiceReachSettingsResponder>.Success(
            ServiceReachSettingsResponder.Of(result.Data)));
    }
}
