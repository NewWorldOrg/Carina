using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Auth;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Auth;

[ApiController]
[Route(OidcHandshake.ConfigRoute)]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetOidcConfigAction(OidcConfigService configuration) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<OidcConfigResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<OidcConfigResponder>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<OidcConfigView> asked = await configuration.ReadAsync(
            OidcHandshake.RedirectUriFor(Request),
            cancellationToken);

        return Ok(BaseResponder<OidcConfigResponder>.Success(OidcConfigResponder.Of(asked.Data!)));
    }
}
