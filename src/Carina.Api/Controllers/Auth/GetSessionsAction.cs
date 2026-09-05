using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Auth;
using Carina.Api.Services;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Auth;

[ApiController]
[Route("api/auth/sessions")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetSessionsAction(AuthSessionService sessions) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<IReadOnlyList<SessionResponder>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<IReadOnlyList<SessionResponder>>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        if (SessionClaims.SessionOf(User) is not { } current)
        {
            return Unauthorized(
                BaseResponder<IReadOnlyList<SessionResponder>>.Error("This request carries no session."));
        }

        ServiceResult<IReadOnlyList<SessionView>> asked = await sessions.ListAsync(current, cancellationToken);

        return Ok(BaseResponder<IReadOnlyList<SessionResponder>>.Success(
            [.. asked.Data!.Select(SessionResponder.Of)]));
    }
}
