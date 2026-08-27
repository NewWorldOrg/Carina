using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Recordings;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Recordings;

[ApiController]
[Route("api/recordings/{id}")]
[EndpointEffect(EndpointEffect.Destructive)]
public sealed class DeleteRecordingAction(RecordingService recordings) : ControllerBase
{
    [HttpDelete]
    [ProducesResponseType<BaseResponder<RecordingDiscardResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<RecordingDiscardResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<RecordingDiscardRefusedResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<RecordingDiscardRefusedResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<RecordingDiscardRefusedResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(string id, CancellationToken cancellationToken)
    {
        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            return BadRequest(BaseResponder<RecordingDiscardResponder>.Error(RecordingIdText.Description));
        }

        ServiceResult<RecordingDiscarded, RecordingFailure> discarded =
            await recordings.DiscardAsync(recordingId, cancellationToken);

        return discarded.IsSuccess
            ? Ok(BaseResponder<RecordingDiscardResponder>.Success(
                RecordingDiscardResponder.Of(discarded.Data!)))
            : StatusCode(
                RecordingStatus.Of(discarded.ErrorType),
                new BaseResponder<RecordingDiscardRefusedResponder>(
                    false,
                    discarded.ErrorMessage!,
                    RecordingDiscardRefusedResponder.Of(recordingId.Wire, discarded.ErrorType)));
    }
}
