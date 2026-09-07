using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Quality;
using Carina.Api.Services;
using Carina.Domain.Quality;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Quality;

[ApiController]
[Route("api/quality/channels")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListQualityChannelsAction(QualityService quality) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<QualityChannelListResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<QualityChannelListResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? until,
        [FromQuery] QualityMetric[]? metric,
        [FromQuery] QualityGroupSort? sort,
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
    {
        ServiceResult<QualityGroupPage> read = await quality.ListChannelsAsync(
            new QualityGroupAsk(from?.UtcDateTime, until?.UtcDateTime, metric, sort, page, perPage),
            cancellationToken);

        return read.IsSuccess
            ? Ok(BaseResponder<QualityChannelListResponder>.Success(QualityChannelListResponder.Of(read.Data!)))
            : BadRequest(BaseResponder<QualityChannelListResponder>.Error(read.ErrorMessage!));
    }
}
