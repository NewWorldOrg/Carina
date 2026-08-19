using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Auth;
using Carina.Api.Services;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Auth;

[ApiController]
[Route("api/auth/password")]
[EndpointEffect(EndpointEffect.Destructive)]
public sealed class ChangePasswordAction(LocalAccountService accounts) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<PasswordChangedResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<PasswordChangedResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<PasswordChangedResponder>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Invoke(
        [FromBody] ChangePasswordRequest? request,
        CancellationToken cancellationToken)
    {
        if (SessionClaims.SubjectOf(User) is not { } subject
            || SessionClaims.SessionOf(User) is not { } current)
        {
            return Unauthorized(
                BaseResponder<PasswordChangedResponder>.Error("This request carries no session."));
        }

        var change = new PasswordChange(
            subject,
            current,
            request?.CurrentPassword ?? string.Empty,
            request?.NewPassword ?? string.Empty);

        ServiceResult<int, PasswordRefusal> asked = await accounts.ChangePasswordAsync(
            change,
            cancellationToken);

        if (asked.IsSuccess)
        {
            return Ok(BaseResponder<PasswordChangedResponder>.Success(
                new PasswordChangedResponder(asked.Data)));
        }

        return asked.ErrorType is PasswordRefusal.WrongPassword
            ? Unauthorized(BaseResponder<PasswordChangedResponder>.Error(asked.ErrorMessage!))
            : BadRequest(BaseResponder<PasswordChangedResponder>.Error(asked.ErrorMessage!));
    }
}
