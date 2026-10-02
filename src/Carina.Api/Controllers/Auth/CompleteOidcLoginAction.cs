using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Services;

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
        NeverStored.Mark(Response);

        ServiceResult<OidcArrival> asked = await logins.CompleteAsync(
            new OidcArrivalAttempt(
                state,
                code,
                OidcHandshake.MarkCarriedBy(Request),
                OidcHandshake.ArrivedAt(Request),
                DeviceLabel.From(Request.Headers.UserAgent.ToString())),
            cancellationToken);

        OidcArrival arrival = asked.Data!;

        if (arrival.Cookie is not { } cookie || arrival.Session is not { } session)
        {
            SignInHappening.Leave(
                HttpContext,
                SignInMoment.TheWayBackFromTheProviderWasRefused,
                reason: arrival.Refusal.ToString());

            return Redirect(LoginRedirect.AfterAFailedSignIn(arrival.ReturnPath));
        }

        SignInHappening.Leave(
            HttpContext,
            SignInMoment.TheWayBackFromTheProviderOpenedASession,
            session.Method,
            session.DeviceLabel);

        Response.Cookies.Append(
            SessionCookie.Name,
            cookie.Value,
            SessionCookie.Carrying(Request.IsHttps, arrival.SessionLifetime));

        return Redirect(arrival.ReturnPath);
    }
}
