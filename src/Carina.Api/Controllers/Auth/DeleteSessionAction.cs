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
        SessionId target;

        try
        {
            target = new SessionId(id);
        }
        catch (ArgumentException)
        {
            return NotFound(BaseResponder<string>.Error(AuthSessionService.NoSuchSession));
        }

        ServiceResult ended = await sessions.RevokeAsync(target, cancellationToken);

        return ended.IsSuccess
            ? NoContent()
            : NotFound(BaseResponder<string>.Error(ended.ErrorMessage!));
    }
}
