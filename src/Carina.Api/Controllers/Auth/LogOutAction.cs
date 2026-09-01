using Carina.Api.Authentication;
using Carina.Api.Responder;
using Carina.Api.Services;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Auth;

[ApiController]
[Route("api/auth/logout")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class LogOutAction(AuthSessionService sessions) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<BaseResponder<string>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        if (SessionClaims.SubjectOf(User) is not { } subject
            || SessionClaims.SessionOf(User) is not { } current)
        {
            return Unauthorized(BaseResponder<string>.Error("This request carries no session."));
        }

        await sessions.LogOutAsync(subject, current, cancellationToken);

        Response.Cookies.Delete(
            SessionCookie.Name,
            SessionCookie.Discarding(Request.IsHttps));

        return NoContent();
    }
}
