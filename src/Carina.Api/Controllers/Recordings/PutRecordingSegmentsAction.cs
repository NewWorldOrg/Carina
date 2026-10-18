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
[Route("api/recordings/{id}/segments")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class PutRecordingSegmentsAction(RecordingSegmentService segments) : ControllerBase
{
    [HttpPut]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<RecordingSegmentsResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<RecordingSegmentsResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<RecordingSegmentsResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<RecordingSegmentsResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(
        string id,
        [FromBody] PutRecordingSegmentsRequest? request,
        CancellationToken cancellationToken)
    {
        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            return BadRequest(BaseResponder<RecordingSegmentsResponder>.Error(RecordingIdText.Description));
        }

        ServiceResult<RecordingSegments, RecordingSegmentFailure> answered = await segments.AnswerAsync(
            recordingId,
            request?.Segments?.Select(Sent).ToList(),
            request?.Confirmed,
            cancellationToken);

        return answered.IsSuccess
            ? Ok(BaseResponder<RecordingSegmentsResponder>.Success(RecordingSegmentsResponder.Of(answered.Data!)))
            : StatusCode(Of(answered.ErrorType), BaseResponder<RecordingSegmentsResponder>.Error(answered.ErrorMessage!));
    }

    private static int Of(RecordingSegmentFailure failure) => failure switch
    {
        RecordingSegmentFailure.NoSuchRecording => StatusCodes.Status404NotFound,
        RecordingSegmentFailure.StillBeingWritten => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    private static SentStretch? Sent(AnsweredSegmentRequest? segment)
        => segment is null ? null : new SentStretch(segment.StartsAtSec, segment.EndsAtSec, WireNameText.Read<SegmentKind>(segment.Kind));
}
