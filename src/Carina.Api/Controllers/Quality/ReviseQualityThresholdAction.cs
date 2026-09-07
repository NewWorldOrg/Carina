using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Quality;
using Carina.Api.Services;
using Carina.Domain.Quality;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Quality;

[ApiController]
[Route("api/quality/thresholds/{key}")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class ReviseQualityThresholdAction(QualityThresholdService thresholds) : ControllerBase
{
    [HttpPatch]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<QualityThresholdResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<QualityThresholdResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<QualityThresholdResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(
        string key,
        [FromBody] ReviseQualityThresholdRequest? request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse(key, ignoreCase: true, out QualityThresholdKey named) || !Enum.IsDefined(named))
        {
            return NotFound(BaseResponder<QualityThresholdResponder>.Error(QualitySaying.NoSuchThreshold()));
        }

        if (request is not { Value: { } value })
        {
            return BadRequest(BaseResponder<QualityThresholdResponder>.Error(
                "A threshold is moved by naming the value it moves to."));
        }

        ServiceResult<QualityThresholdBook, QualityThresholdFailure> revised =
            await thresholds.ReviseAsync(named, value, cancellationToken);

        return revised.IsSuccess
            ? Ok(BaseResponder<QualityThresholdResponder>.Success(QualityThresholdResponder.Of(revised.Data!)))
            : BadRequest(BaseResponder<QualityThresholdResponder>.Error(revised.ErrorMessage!));
    }
}
