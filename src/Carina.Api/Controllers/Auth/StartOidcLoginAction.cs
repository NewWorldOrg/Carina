using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Services;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Auth;

[ApiController]
[Route(OidcHandshake.StartRoute)]
[EndpointEffect(EndpointEffect.Reading)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class StartOidcLoginAction(OidcLoginService logins) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Invoke(
        [FromQuery(Name = LoginRedirect.ReturnKey)] string? next,
        CancellationToken cancellationToken)
    {
        ServiceResult<OidcStart, OidcRefusal> asked = await logins.StartAsync(
            new OidcStartAttempt(
                OidcHandshake.MarkCarriedBy(Request),
                next,
                OidcHandshake.RedirectUriFor(Request)),
            cancellationToken);

        if (asked.Data is not { } start)
        {
            return Redirect(LoginRedirect.AfterAFailedSignIn(next));
        }

        Response.Cookies.Append(
            OidcHandshake.MarkNameFor(Request.IsHttps),
            start.BrowserMark,
            OidcHandshake.MarkCookie(Request.IsHttps, start.MarkLifetime));

        return Redirect(start.Authorize.ToString());
    }
}
