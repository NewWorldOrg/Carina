using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Recordings;
using Carina.Api.Services;
using Carina.Domain.Recordings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Recordings;

[ApiController]
[Route("api/recordings/{id}")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetRecordingAction(RecordingService recordings) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<RecordingDetailResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<RecordingDetailResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<RecordingDetailResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(string id, CancellationToken cancellationToken)
    {
        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            return BadRequest(BaseResponder<RecordingDetailResponder>.Error(RecordingIdText.Description));
        }

        ServiceResult<Recording, RecordingFailure> found = await recordings.FindAsync(recordingId, cancellationToken);

        return found.IsSuccess
            ? Ok(BaseResponder<RecordingDetailResponder>.Success(RecordingDetailResponder.Of(found.Data!)))
            : StatusCode(
                RecordingStatus.Of(found.ErrorType),
                BaseResponder<RecordingDetailResponder>.Error(found.ErrorMessage!));
    }
}
