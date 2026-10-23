using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Encoding;
using Carina.Api.Services;
using Carina.Domain.Encodings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Encoding;

[ApiController]
[Route("api/encoding/profiles/defaults")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetEncodeProfileDefaultsAction(EncodeProfileService profiles) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<EncodeProfileDefaultsResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<EncodeProfileDefaults> read = await profiles.DefaultsAsync(cancellationToken);

        return Ok(BaseResponder<EncodeProfileDefaultsResponder>.Success(EncodeProfileDefaultsResponder.Of(read.Data!)));
    }
}
