using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Services;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Auth;

[ApiController]
[Route("api/auth/sessions/{id}")]
[EndpointEffect(EndpointEffect.Destructive)]
public sealed class DeleteSessionAction(AuthSessionService sessions) : ControllerBase
{
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<BaseResponder<string>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<BaseResponder<string>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(string id, CancellationToken cancellationToken)
    {
        SessionHandle target;

        try
        {
            target = new SessionHandle(id);
        }
        catch (ArgumentException)
        {
            return NotFound(BaseResponder<string>.Error(AuthSessionService.NoSuchSession));
        }

        ServiceResult<EndedSessions> ended = await sessions.RevokeAsync(target, cancellationToken);

        if (!ended.IsSuccess)
        {
            return NotFound(BaseResponder<string>.Error(ended.ErrorMessage!));
        }

        SignInHappening.Leave(
            HttpContext,
            target.Equals(SessionClaims.SessionOf(User))
                ? SignInMoment.TheSessionRevokedItself
                : SignInMoment.TheSessionRevokedAnother,
            SessionClaims.MethodOf(User),
            SessionClaims.DeviceOf(User),
            ended: ended.Data);

        return NoContent();
    }
}
