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
/// Draws the pictures of several recordings again, each by its id, up to <see cref="ThumbnailRemakes.MostAtOnce"/>
/// in one request. Each id is taken on its own, through the same path a person asks for one recording's
/// picture by hand, and the answer holds one row per id in the order asked, so a refusal of one leaves the
/// rest drawn.
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
