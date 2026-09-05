using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Encoding;
using Carina.Api.Services;
using Carina.Domain.Encodings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Encoding;

[ApiController]
[Route("api/encoding/jobs/{id:guid}/cancel")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class CancelEncodeJobAction(EncodeJobService jobs) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<EncodeJobResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<EncodeJobResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<EncodeJobResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<EncodeJobResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(Guid id, CancellationToken cancellationToken)
    {
        if (EncodingIdText.Job(id) is not { } jobId)
        {
            return BadRequest(BaseResponder<EncodeJobResponder>.Error(EncodingIdText.Description));
        }

        ServiceResult<EncodeJobView, EncodingFailure> cancelled = await jobs.CancelAsync(jobId, cancellationToken);

        return cancelled.IsSuccess
            ? Ok(BaseResponder<EncodeJobResponder>.Success(EncodeJobResponder.Of(cancelled.Data!)))
            : StatusCode(EncodingStatus.Of(cancelled.ErrorType), BaseResponder<EncodeJobResponder>.Error(cancelled.ErrorMessage!));
    }
}
