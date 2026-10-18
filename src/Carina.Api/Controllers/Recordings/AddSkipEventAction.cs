using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Segments;
using Carina.Api.Services;
using Carina.Domain.Segments;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Recordings;

[ApiController]
[Route("api/recordings/{id}/skips")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class AddSkipEventAction(SkipEventService skips) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<SkipEventResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<SkipEventResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<SkipEventResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<SkipEventResponder>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Invoke(
        string id,
        [FromBody] AddSkipEventRequest? request,
        CancellationToken cancellationToken)
    {
        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            return BadRequest(BaseResponder<SkipEventResponder>.Error(RecordingIdText.Description));
        }

        SentSkip sent = new(
            WireNameText.Read<ViewerActionKind>(request?.Event),
            WireNameText.Read<SegmentKind>(request?.Kind),
            request?.FromSec,
            request?.ToSec,
            request?.ModelVersion);
        ServiceResult<SkipEvent, SkipEventFailure> taken = await skips.TakeAsync(recordingId, sent, cancellationToken);

        return taken.IsSuccess
            ? Ok(BaseResponder<SkipEventResponder>.Success(SkipEventResponder.Of(taken.Data!)))
            : StatusCode(Of(taken.ErrorType), BaseResponder<SkipEventResponder>.Error(taken.ErrorMessage!));
    }

    private static int Of(SkipEventFailure failure) => failure switch
    {
        SkipEventFailure.NoSuchRecording => StatusCodes.Status404NotFound,
        SkipEventFailure.TooMany => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status400BadRequest,
    };
}
