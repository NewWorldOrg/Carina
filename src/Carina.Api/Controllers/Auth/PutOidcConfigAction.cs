using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Auth;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Auth;

[ApiController]
[Route(OidcHandshake.ConfigRoute)]
[EndpointEffect(EndpointEffect.Destructive)]
public sealed class PutOidcConfigAction(OidcConfigService configuration) : ControllerBase
{
    [HttpPut]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<OidcConfigResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<OidcConfigResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<OidcConfigResponder>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Invoke(
        [FromBody] OidcConfigRequest? request,
        CancellationToken cancellationToken)
    {
        ServiceResult<OidcConfigView> asked = await configuration.SaveAsync(
            new OidcConfigChange(
                request?.DiscoveryUrl,
                request?.ClientId,
                request?.ClientSecret,
                request?.AllowedGroups,
                request?.AllowedHostedDomains),
            OidcHandshake.RedirectUriFor(Request),
            cancellationToken);

        if (asked.Data is not { } saved)
        {
            return BadRequest(BaseResponder<OidcConfigResponder>.Error(asked.ErrorMessage!));
        }

        return Ok(BaseResponder<OidcConfigResponder>.Success(OidcConfigResponder.Of(saved)));
    }
}
