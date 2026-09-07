using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Quality;
using Carina.Api.Services;
using Carina.Domain.Quality;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Quality;

[ApiController]
[Route("api/quality/recordings")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListQualityRecordingsAction(QualityService quality) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<QualityRecordingListResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<QualityRecordingListResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? until,
        [FromQuery] QualityMetric[]? metric,
        [FromQuery] QualityRecordingSort? sort,
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
    {
        ServiceResult<QualityRecordingPage> read = await quality.ListRecordingsAsync(
            new QualityRecordingAsk(from?.UtcDateTime, until?.UtcDateTime, metric, sort, page, perPage),
            cancellationToken);

        return read.IsSuccess
            ? Ok(BaseResponder<QualityRecordingListResponder>.Success(QualityRecordingListResponder.Of(read.Data!)))
            : BadRequest(BaseResponder<QualityRecordingListResponder>.Error(read.ErrorMessage!));
    }
}
