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
[Route("api/recordings/{id}/seeks")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class AddSeekEventAction(SeekEventService seeks) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<SeekEventResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<SeekEventResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<SeekEventResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<SeekEventResponder>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Invoke(
        string id,
        [FromBody] AddSeekEventRequest? request,
        CancellationToken cancellationToken)
    {
        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            return BadRequest(BaseResponder<SeekEventResponder>.Error(RecordingIdText.Description));
        }

        SentSeek sent = new(
            WireNameText.Read<SeekAction>(request?.Event),
            WireNameText.Read<SeekMeans>(request?.Means),
            request?.FromSec,
            request?.ToSec);
        ServiceResult<SeekEvent, SeekEventFailure> taken = await seeks.TakeAsync(recordingId, sent, cancellationToken);

        return taken.IsSuccess
            ? Ok(BaseResponder<SeekEventResponder>.Success(SeekEventResponder.Of(taken.Data!)))
            : StatusCode(Of(taken.ErrorType), BaseResponder<SeekEventResponder>.Error(taken.ErrorMessage!));
    }

    private static int Of(SeekEventFailure failure) => failure switch
    {
        SeekEventFailure.NoSuchRecording => StatusCodes.Status404NotFound,
        SeekEventFailure.TooMany => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status400BadRequest,
    };
}
