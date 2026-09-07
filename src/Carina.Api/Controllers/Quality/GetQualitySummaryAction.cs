using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Quality;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Quality;

[ApiController]
[Route("api/quality/summary")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetQualitySummaryAction(QualityService quality) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<QualitySummaryResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<QualitySummaryResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? until,
        CancellationToken cancellationToken)
    {
        ServiceResult<QualitySummaryView> read = await quality.SummariseAsync(
            from?.UtcDateTime,
            until?.UtcDateTime,
            cancellationToken);

        return read.IsSuccess
            ? Ok(BaseResponder<QualitySummaryResponder>.Success(QualitySummaryResponder.Of(read.Data!)))
            : BadRequest(BaseResponder<QualitySummaryResponder>.Error(read.ErrorMessage!));
    }
}
