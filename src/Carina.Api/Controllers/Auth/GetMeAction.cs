using Carina.Api.Authentication;
using Carina.Api.Responder;
using Carina.Api.Responder.Auth;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Auth;

[ApiController]
[Route("api/auth/me")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetMeAction : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<MeResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<MeResponder>>(StatusCodes.Status401Unauthorized)]
    public IActionResult Invoke()
    {
        if (SessionClaims.SubjectOf(User) is not { } subject
            || SessionClaims.MethodOf(User) is not { } method)
        {
            return Unauthorized(BaseResponder<MeResponder>.Error("This request carries no session."));
        }

        return Ok(BaseResponder<MeResponder>.Success(new MeResponder(subject.Value, method)));
    }
}
