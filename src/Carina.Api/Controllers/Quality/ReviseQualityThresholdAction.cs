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
        if (!Enum.TryParse(key, ignoreCase: true, out QualityThresholdKey named)
            || !Enum.IsDefined(named)
            || !QualityThresholdShapes.Of(named).Consulted)
        {
            return NotFound(BaseResponder<QualityThresholdResponder>.Error(QualitySaying.NoSuchThreshold()));
        }

        return request switch
        {
            { Value: { } value, ByHand: null or true } => Answered(await thresholds.ReviseAsync(named, value, cancellationToken)),
            { Value: null, ByHand: false } => Answered(await thresholds.ReleaseAsync(named, cancellationToken)),
            _ => BadRequest(BaseResponder<QualityThresholdResponder>.Error(
                "A threshold is moved by naming the value it moves to, or let go of by saying it is no longer set by hand.")),
        };
    }

    private IActionResult Answered(ServiceResult<QualityThresholdBook, QualityThresholdFailure> revised)
        => revised.IsSuccess
            ? Ok(BaseResponder<QualityThresholdResponder>.Success(QualityThresholdResponder.Of(revised.Data!)))
            : BadRequest(BaseResponder<QualityThresholdResponder>.Error(revised.ErrorMessage!));
}
