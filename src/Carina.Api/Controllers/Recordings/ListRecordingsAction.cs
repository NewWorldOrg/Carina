using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Recordings;
using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Recordings;

[ApiController]
[Route("api/recordings")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListRecordingsAction(RecordingService recordings) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<RecordingListResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<RecordingListResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromQuery] RecordingStanding? standing,
        [FromQuery] RecordingOutcome[]? outcome,
        [FromQuery] DropReading? drops,
        [FromQuery] string[]? channel,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] RecordingSort sort,
        [FromQuery] bool descending,
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
    {
        RecordingQuery? asked = ProgrammeServiceText.Every(channel) is { } channels
            ? RecordingQuery.For(
                from?.UtcDateTime,
                to?.UtcDateTime,
                sort,
                descending,
                page,
                perPage,
                new RecordingConditions
                {
                    Standing = standing,
                    Outcomes = outcome,
                    Drops = drops,
                    Channels = channels,
                })
            : null;

        if (asked is null)
        {
            return BadRequest(BaseResponder<RecordingListResponder>.Error(Refusal));
        }

        ServiceResult<PaginatedList<Recording>> found = await recordings.ListAsync(asked, cancellationToken);

        return Ok(BaseResponder<RecordingListResponder>.Success(RecordingListResponder.Of(found.Data!)));
    }

    private static string Refusal
        => "A page is asked for by a page number of at least 1, and a page size above "
            + $"{RecordingQuery.MostPerPage} is cut down to it and answered as the size that was used. "
            + $"A span runs forwards and reaches back at most {RecordingQuery.LongestSpan.TotalDays:0} days, "
            + $"at most {RecordingQuery.MostChannels} channels are named as network-service, "
            + "and the state, the outcome, the drop reading and the sort are each one of the values this "
            + "endpoint names. A recording still being written has no outcome yet, so asking for both at once "
            + "asks for nothing.";
}
