using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Encoding;
using Carina.Api.Services;
using Carina.Domain.Encodings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Encoding;

[ApiController]
[Route("api/encoding/profiles")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListEncodeProfilesAction(EncodeProfileService profiles) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<EncodeProfileListResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<IReadOnlyList<EncodeProfile>> listed = await profiles.ListAsync(cancellationToken);

        return Ok(BaseResponder<EncodeProfileListResponder>.Success(EncodeProfileListResponder.Of(listed.Data!)));
    }
}
