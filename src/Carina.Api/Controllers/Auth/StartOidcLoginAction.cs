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
        NeverStored.Mark(Response);

        ServiceResult<OidcStart, OidcRefusal> asked = await logins.StartAsync(
            new OidcStartAttempt(
                OidcHandshake.MarkCarriedBy(Request),
                next,
                OidcHandshake.ArrivedAt(Request)),
            cancellationToken);

        if (asked.Data is not { } start)
        {
            SignInHappening.Leave(
                HttpContext,
                SignInMoment.TheWayToTheProviderCouldNotBeOpened,
                reason: asked.ErrorType.ToString());

            return Redirect(LoginRedirect.AfterAFailedSignIn(next));
        }

        SignInHappening.Leave(HttpContext, SignInMoment.TheWayToTheProviderWasOpened);

        Response.Cookies.Append(
            OidcHandshake.MarkName,
            start.BrowserMark,
            OidcHandshake.MarkCookie(Request.IsHttps, start.MarkLifetime));

        return Redirect(start.Authorize.AbsoluteUri);
    }
}
