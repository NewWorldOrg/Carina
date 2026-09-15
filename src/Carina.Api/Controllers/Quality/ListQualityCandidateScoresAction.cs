using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Quality;
using Carina.Api.Services;
using Carina.Domain.Channels;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Quality;

[ApiController]
[Route("api/quality/candidate-scores")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListQualityCandidateScoresAction(QualityCandidateScoreService scores) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<QualityCandidateScoreListResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<IReadOnlyList<CandidateChannel>> read = await scores.ListAsync(cancellationToken);

        return Ok(BaseResponder<QualityCandidateScoreListResponder>.Success(
            QualityCandidateScoreListResponder.Of(read.Data!)));
    }
}
