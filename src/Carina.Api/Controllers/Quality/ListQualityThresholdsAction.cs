using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Quality;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Quality;

[ApiController]
[Route("api/quality/thresholds")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListQualityThresholdsAction(QualityThresholdService thresholds) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<QualityThresholdListResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<IReadOnlyList<QualityThresholdBook>> read = await thresholds.ListAsync(cancellationToken);

        return Ok(BaseResponder<QualityThresholdListResponder>.Success(
            QualityThresholdListResponder.Of(read.Data!)));
    }
}
