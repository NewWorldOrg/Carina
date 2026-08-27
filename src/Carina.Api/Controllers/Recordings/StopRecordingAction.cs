using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Recordings;
using Carina.Api.Services;
using Carina.Domain.Recordings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Recordings;

[ApiController]
[Route("api/recordings/{id}/stop")]
[EndpointEffect(EndpointEffect.Destructive)]
public sealed class StopRecordingAction(RecordingService recordings) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<RecordingStopResponder>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<BaseResponder<RecordingStopResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<RecordingStopResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<RecordingStopResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<RecordingStopResponder>>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<BaseResponder<RecordingStopResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(
        string id,
        [FromBody] StopRecordingRequest? request,
        CancellationToken cancellationToken)
    {
        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            return BadRequest(BaseResponder<RecordingStopResponder>.Error(RecordingIdText.Description));
        }

        if (RecordingStopReason.Read(request?.Reason) is not { } reason)
        {
            return BadRequest(BaseResponder<RecordingStopResponder>.Error(
                "reason: a recording is never stopped by nobody for nothing, so a stop carries a reason of at "
                + $"least one letter and at most {RecordingStopReason.MaxLength}, which is kept on the recording."));
        }

        ServiceResult<RecordingStopAsked, RecordingFailure> stopped =
            await recordings.StopAsync(recordingId, reason, cancellationToken);

        return stopped.IsSuccess
            ? Accepted(BaseResponder<RecordingStopResponder>.Success(
                RecordingStopResponder.Of(stopped.Data!)))
            : StatusCode(
                RecordingStatus.Of(stopped.ErrorType),
                BaseResponder<RecordingStopResponder>.Error(stopped.ErrorMessage!));
    }
}
