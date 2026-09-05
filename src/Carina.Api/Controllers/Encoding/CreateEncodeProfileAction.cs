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
[Route("api/encoding/profiles")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class CreateEncodeProfileAction(EncodeProfileService profiles) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<EncodeProfileResponder>>(StatusCodes.Status201Created)]
    [ProducesResponseType<BaseResponder<EncodeProfileResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke([FromBody] CreateEncodeProfileRequest? request, CancellationToken cancellationToken)
    {
        if (request is not { Codec: { } codec, Resolution: { } resolution, Deinterlace: { } deinterlace, RateFactor: { } rateFactor, Quantiser: { } quantiser })
        {
            return BadRequest(BaseResponder<EncodeProfileResponder>.Error(
                "A profile is defined by label, codec, resolution, deinterlace, rateFactor and quantiser, and every one of them is given."));
        }

        ServiceResult<EncodeProfile, EncodingFailure> defined = await profiles.DefineAsync(
            new EncodeProfileDraft(request.Label, codec, resolution, deinterlace, rateFactor, quantiser),
            cancellationToken);

        return defined.IsSuccess
            ? Created(
                new Uri("/api/encoding/profiles", UriKind.Relative),
                BaseResponder<EncodeProfileResponder>.Success(EncodeProfileResponder.Of(defined.Data!)))
            : StatusCode(EncodingStatus.Of(defined.ErrorType), BaseResponder<EncodeProfileResponder>.Error(defined.ErrorMessage!));
    }
}
