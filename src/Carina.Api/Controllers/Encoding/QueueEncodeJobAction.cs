using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Encoding;
using Carina.Api.Services;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Encoding;

/// <summary>
/// Queues one recording, named by its id, for one destination. There is no way in that takes more
/// than one recording (BR-ED2-008).
/// </summary>
[ApiController]
[Route("api/encoding/jobs")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class QueueEncodeJobAction(EncodeJobService jobs) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<EncodeJobResponder>>(StatusCodes.Status201Created)]
    [ProducesResponseType<BaseResponder<EncodeJobResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<EncodeJobResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<EncodeJobResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke([FromBody] QueueEncodeJobRequest? request, CancellationToken cancellationToken)
    {
        if (RecordingIdText.Read(request?.RecordingId) is not { } recording)
        {
            return BadRequest(BaseResponder<EncodeJobResponder>.Error("recordingId: " + RecordingIdText.Description));
        }

        if (EncodingIdText.Destination(request?.DestinationId) is not { } destination)
        {
            return BadRequest(BaseResponder<EncodeJobResponder>.Error("destinationId: " + EncodingIdText.Description));
        }

        if (request?.ProfileId is { } named && EncodingIdText.Profile(named) is null)
        {
            return BadRequest(BaseResponder<EncodeJobResponder>.Error("profileId: " + EncodingIdText.Description));
        }

        ServiceResult<EncodeJobView, EncodingFailure> queued = await jobs.QueueAsync(
            new EncodeJobDraft(recording, EncodingIdText.Profile(request?.ProfileId), destination),
            cancellationToken);

        return queued.IsSuccess
            ? Created(
                new Uri("/api/encoding/jobs", UriKind.Relative),
                BaseResponder<EncodeJobResponder>.Success(EncodeJobResponder.Of(queued.Data!)))
            : StatusCode(EncodingStatus.Of(queued.ErrorType), BaseResponder<EncodeJobResponder>.Error(queued.ErrorMessage!));
    }
}
