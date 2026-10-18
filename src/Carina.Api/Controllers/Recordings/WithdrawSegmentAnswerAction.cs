using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Segments;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Recordings;

[ApiController]
[Route("api/recordings/{id}/segments/answer")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class WithdrawSegmentAnswerAction(RecordingSegmentService segments) : ControllerBase
{
    [HttpDelete]
    [ProducesResponseType<BaseResponder<RecordingSegmentsResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<RecordingSegmentsResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<RecordingSegmentsResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(string id, CancellationToken cancellationToken)
    {
        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            return BadRequest(BaseResponder<RecordingSegmentsResponder>.Error(RecordingIdText.Description));
        }

        ServiceResult<RecordingSegments, RecordingSegmentFailure> withdrawn = await segments.WithdrawAsync(recordingId, cancellationToken);

        return withdrawn.IsSuccess
            ? Ok(BaseResponder<RecordingSegmentsResponder>.Success(RecordingSegmentsResponder.Of(withdrawn.Data!)))
            : NotFound(BaseResponder<RecordingSegmentsResponder>.Error(withdrawn.ErrorMessage!));
    }
}
