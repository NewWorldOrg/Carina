using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Encoding;
using Carina.Api.Services;
using Carina.Domain.Encodings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Encoding;

[ApiController]
[Route("api/encoding/profiles/{id:guid}")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class ReviseEncodeProfileAction(EncodeProfileService profiles) : ControllerBase
{
    [HttpPatch]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<EncodeProfileResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<EncodeProfileResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<EncodeProfileResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<EncodeProfileResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(
        Guid id,
        [FromBody] ReviseEncodeProfileRequest? request,
        CancellationToken cancellationToken)
    {
        if (EncodingIdText.Profile(id) is not { } profileId)
        {
            return BadRequest(BaseResponder<EncodeProfileResponder>.Error(EncodingIdText.Description));
        }

        if (request is not { Codec: { } codec, Resolution: { } resolution, Deinterlace: { } deinterlace, RateFactor: { } rateFactor, Quantiser: { } quantiser })
        {
            return BadRequest(BaseResponder<EncodeProfileResponder>.Error(
                "A profile is defined by label, codec, resolution, deinterlace, rateFactor and quantiser, and a change carries every one of them rather than the ones that moved."));
        }

        ServiceResult<EncodeProfile, EncodingFailure> revised = await profiles.ReviseAsync(
            profileId,
            new EncodeProfileDraft(request.Label, codec, resolution, deinterlace, rateFactor, quantiser),
            cancellationToken);

        return revised.IsSuccess
            ? Ok(BaseResponder<EncodeProfileResponder>.Success(EncodeProfileResponder.Of(revised.Data!)))
            : StatusCode(EncodingStatus.Of(revised.ErrorType), BaseResponder<EncodeProfileResponder>.Error(revised.ErrorMessage!));
    }
}
