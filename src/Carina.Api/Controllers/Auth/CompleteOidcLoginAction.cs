using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Services;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Auth;

[ApiController]
[Route(OidcHandshake.CallbackRoute)]
[EndpointEffect(EndpointEffect.Reading)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class CompleteOidcLoginAction(OidcLoginService logins) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Invoke(
        [FromQuery(Name = OidcHandshake.StateKey)] string? state,
        [FromQuery(Name = OidcHandshake.CodeKey)] string? code,
        CancellationToken cancellationToken)
    {
        ServiceResult<OidcArrival, OidcRefusal> asked = await logins.CompleteAsync(
            new OidcArrivalAttempt(
                state,
                code,
                OidcHandshake.MarkCarriedBy(Request),
                OidcHandshake.RedirectUriFor(Request),
                DeviceLabel.From(Request.Headers.UserAgent.ToString())),
            cancellationToken);

        if (asked.Data is not { } arrival)
        {
            return Redirect(LoginRedirect.AfterAFailedSignIn(null));
        }

        Response.Cookies.Append(
            SessionCookie.NameFor(Request.IsHttps),
            arrival.Session.Id.Value,
            SessionCookie.Carrying(Request.IsHttps, arrival.SessionLifetime));

        return Redirect(arrival.ReturnPath);
    }
}
