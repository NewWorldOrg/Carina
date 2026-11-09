using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Recordings;
using Carina.Api.Services;
using Carina.Domain.Recordings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Recordings;

/// <summary>
/// Puts several recordings back in the queue the thumbnail pass reads, each by its id, up to
/// <see cref="ThumbnailRemakes.MostAtOnce"/> in one request. Nothing is drawn here: each id is checked as the
/// single recording's action checks it, then marked as waiting for a picture, and the pass draws them in
/// order. A row answered <c>queued</c> is drawn by that pass: it is woken as soon as one row was queued and
/// takes eight recordings a run, then keeps to its cadence of one run every five minutes, so a hundred
/// rows take about an hour. The answer holds one row per id in the order asked, so a refusal of one leaves
/// the rest queued.
/// </summary>
[ApiController]
[Route("api/recordings/thumbnails/remake")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class RemakeThumbnailsAction(RecordingService recordings) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<ThumbnailRemakeListResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<ThumbnailRemakeListResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke([FromBody] RemakeThumbnailsRequest? request, CancellationToken cancellationToken)
    {
        if (request?.Ids is not { Count: > 0 and <= ThumbnailRemakes.MostAtOnce } asked)
        {
            return BadRequest(BaseResponder<ThumbnailRemakeListResponder>.Error("ids: " + ThumbnailRemakes.Description));
        }

        RecordingId[] ids = [.. asked.Select(RecordingIdText.Read).OfType<RecordingId>()];

        if (ids.Length != asked.Count)
        {
            return BadRequest(BaseResponder<ThumbnailRemakeListResponder>.Error("ids: " + RecordingIdText.Description));
        }

        ServiceResult<IReadOnlyList<ThumbnailRemakeRow>> remade = await recordings.RemakeThumbnailsAsync(ids, cancellationToken);

        return Ok(BaseResponder<ThumbnailRemakeListResponder>.Success(ThumbnailRemakeListResponder.Of(remade.Data!)));
    }
}
