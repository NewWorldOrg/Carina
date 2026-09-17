using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Playback;
using Carina.Api.Services;
using Carina.Domain.Viewing;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Videos;

[ApiController]
[Route("api/videos/{id}/position")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class PutPlaybackPositionAction(PlaybackPositionService positions) : ControllerBase
{
    [HttpPut]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<PlaybackPositionResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<PlaybackPositionResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<PlaybackPositionResponder>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<BaseResponder<PlaybackPositionResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<PlaybackPositionResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(
        string id,
        [FromBody] PutPlaybackPositionRequest? request,
        CancellationToken cancellationToken)
    {
        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            return BadRequest(BaseResponder<PlaybackPositionResponder>.Error(RecordingIdText.Description));
        }

        if (SessionClaims.SubjectOf(User) is not { } viewer)
        {
            return Unauthorized(
                BaseResponder<PlaybackPositionResponder>.Error("This request carries no session."));
        }

        ServiceResult<PlaybackPosition, PlaybackPositionRefusal> kept = await positions.KeepAsync(
            recordingId,
            viewer,
            request?.PositionSec,
            cancellationToken);

        return kept.IsSuccess
            ? Ok(BaseResponder<PlaybackPositionResponder>.Success(PlaybackPositionResponder.Of(kept.Data!)))
            : StatusCode(
                Of(kept.ErrorType),
                BaseResponder<PlaybackPositionResponder>.Error(kept.ErrorMessage!));
    }

    private static int Of(PlaybackPositionRefusal refusal) => refusal switch
    {
        PlaybackPositionRefusal.NoSuchRecording => StatusCodes.Status404NotFound,
        PlaybackPositionRefusal.StillBeingWritten => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };
}
