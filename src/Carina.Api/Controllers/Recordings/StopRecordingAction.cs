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
    [ProducesResponseType<BaseResponder<RecordingDetailResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<RecordingDetailResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<RecordingDetailResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<RecordingDetailResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<RecordingDetailResponder>>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<BaseResponder<RecordingDetailResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(
        string id,
        [FromBody] StopRecordingRequest? request,
        CancellationToken cancellationToken)
    {
        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            return BadRequest(BaseResponder<RecordingDetailResponder>.Error(RecordingIdText.Description));
        }

        if (RecordingStopReason.Read(request?.Reason) is not { } reason)
        {
            return BadRequest(BaseResponder<RecordingDetailResponder>.Error(
                "reason: a recording is never stopped by nobody for nothing, so a stop carries a reason of at "
                + $"least one letter and at most {RecordingStopReason.MaxLength}, which is kept on the recording."));
        }

        ServiceResult<Recording, RecordingFailure> stopped =
            await recordings.StopAsync(recordingId, reason, cancellationToken);

        return stopped.IsSuccess
            ? Ok(BaseResponder<RecordingDetailResponder>.Success(RecordingDetailResponder.Of(stopped.Data!)))
            : StatusCode(
                RecordingStatus.Of(stopped.ErrorType),
                BaseResponder<RecordingDetailResponder>.Error(stopped.ErrorMessage!));
    }
}
