using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Quality;
using Carina.Api.Services;
using Carina.Domain.Quality;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Quality;

[ApiController]
[Route("api/quality/tuners")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListQualityTunersAction(QualityService quality) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<QualityTunerListResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<QualityTunerListResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? until,
        [FromQuery] QualityMetric[]? metric,
        [FromQuery] QualityGroupSort? sort,
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
    {
        ServiceResult<QualityTunerPage> read = await quality.ListTunersAsync(
            new QualityGroupAsk(from?.UtcDateTime, until?.UtcDateTime, metric, sort, page, perPage),
            cancellationToken);

        return read.IsSuccess
            ? Ok(BaseResponder<QualityTunerListResponder>.Success(QualityTunerListResponder.Of(read.Data!)))
            : BadRequest(BaseResponder<QualityTunerListResponder>.Error(read.ErrorMessage!));
    }
}
