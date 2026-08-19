using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Auth;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Auth;

[ApiController]
[Route(SignInOptions.Route)]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetSignInOptionsAction(OidcConfigService configuration) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<SignInOptionsResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<SignInOptionsView> asked =
            await configuration.ReadSignInOptionsAsync(cancellationToken);

        return Ok(BaseResponder<SignInOptionsResponder>.Success(SignInOptionsResponder.Of(asked.Data!)));
    }
}
