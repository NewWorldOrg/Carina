using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Tuners;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Tuners;

[ApiController]
[Route("api/tuners/health")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetTunerHealthAction(TunerHealthService tunerHealthService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<TunerHealthResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<TunerHealthResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<TunerHealthView, TunerHealthFailure> result =
            await tunerHealthService.ReadAsync(cancellationToken);

        if (!result.IsSuccess)
        {
            return StatusCode(
                TunerHealthStatus.Of(result.ErrorType),
                BaseResponder<TunerHealthResponder>.Error(result.ErrorMessage!));
        }

        return Ok(BaseResponder<TunerHealthResponder>.Success(TunerHealthResponder.Of(result.Data!)));
    }
}
