using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Playback;
using Carina.Api.Services;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Videos;

[ApiController]
[Route("api/videos/{id}/ticket")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class IssueVideoTicketAction(PlaybackTicketService tickets) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<PlaybackTicketResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<PlaybackTicketResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<PlaybackTicketResponder>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<BaseResponder<PlaybackTicketResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<PlaybackTicketResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<PlaybackTicketResponder>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Invoke(string id, CancellationToken cancellationToken)
    {
        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            return BadRequest(BaseResponder<PlaybackTicketResponder>.Error(RecordingIdText.Description));
        }

        if (SessionClaims.SubjectOf(User) is not { } watcher)
        {
            return Unauthorized(BaseResponder<PlaybackTicketResponder>.Error("This request carries no session."));
        }

        ServiceResult<IssuedPlaybackTicket, PlaybackTicketRefusal> issued =
            await tickets.IssueAsync(recordingId, watcher, cancellationToken);

        return issued.IsSuccess
            ? Ok(BaseResponder<PlaybackTicketResponder>.Success(
                PlaybackTicketResponder.Of(issued.Data!)))
            : StatusCode(
                Of(issued.ErrorType),
                BaseResponder<PlaybackTicketResponder>.Error(issued.ErrorMessage!));
    }

    private static int Of(PlaybackTicketRefusal refusal) => refusal switch
    {
        PlaybackTicketRefusal.NoSuchRecording => StatusCodes.Status404NotFound,
        PlaybackTicketRefusal.StillBeingWritten => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status429TooManyRequests,
    };
}
