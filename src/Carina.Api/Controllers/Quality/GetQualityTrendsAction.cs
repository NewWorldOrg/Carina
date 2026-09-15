using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Quality;
using Carina.Api.Services;
using Carina.Domain.Quality;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Quality;

[ApiController]
[Route("api/quality/trends")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetQualityTrendsAction(QualityService quality) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<QualityTrendResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<QualityTrendResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromQuery] int? days,
        [FromQuery] QualityTrendSubject? subject,
        CancellationToken cancellationToken)
    {
        ServiceResult<QualityTrendView> read = await quality.TrendAsync(days, subject, cancellationToken);

        return read.IsSuccess
            ? Ok(BaseResponder<QualityTrendResponder>.Success(QualityTrendResponder.Of(read.Data!)))
            : BadRequest(BaseResponder<QualityTrendResponder>.Error(read.ErrorMessage!));
    }
}
