using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Recordings;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Recordings;

[ApiController]
[Route("api/recordings/{id}/thumbnail")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class RemakeThumbnailAction(RecordingService recordings) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<ThumbnailRemakeResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<ThumbnailRemakeResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<ThumbnailRemakeResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<ThumbnailRemakeResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<ThumbnailRemakeResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(string id, CancellationToken cancellationToken)
    {
        if (RecordingIdText.Read(id) is not { } recordingId)
        {
            return BadRequest(BaseResponder<ThumbnailRemakeResponder>.Error(RecordingIdText.Description));
        }

        ServiceResult<ThumbnailRemade, RecordingFailure> remade =
            await recordings.RemakeThumbnailAsync(recordingId, cancellationToken);

        return remade.IsSuccess
            ? Ok(BaseResponder<ThumbnailRemakeResponder>.Success(ThumbnailRemakeResponder.Of(remade.Data!)))
            : StatusCode(
                RecordingStatus.Of(remade.ErrorType),
                BaseResponder<ThumbnailRemakeResponder>.Error(remade.ErrorMessage!));
    }
}
