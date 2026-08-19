using System.Globalization;

using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Auth;
using Carina.Api.Services;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Controllers.Auth;

[ApiController]
[Route("api/auth/login")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class LogInAction(LocalAccountService accounts, TimeProvider clock) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<MeResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<MeResponder>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<BaseResponder<MeResponder>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Invoke(
        [FromBody] LoginRequest? request,
        CancellationToken cancellationToken)
    {
        var attempt = new LoginAttempt(
            request?.Username ?? string.Empty,
            request?.Password ?? string.Empty,
            DeviceLabel.From(Request.Headers.UserAgent.ToString()),
            Caller());

        ServiceResult<LoginOutcome> asked = await accounts.LogInAsync(attempt, cancellationToken);
        LoginOutcome outcome = asked.Data!;

        if (outcome.RetryAt is { } retryAt)
        {
            Response.Headers[HeaderNames.RetryAfter] = Patience(retryAt);

            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                BaseResponder<MeResponder>.Error(LocalAccountService.TheRefusalForTooManyAttempts));
        }

        if (outcome.Session is not { } session)
        {
            return Unauthorized(
                BaseResponder<MeResponder>.Error(LocalAccountService.TheSameRefusalForEveryBadLogin));
        }

        Response.Cookies.Append(
            SessionCookie.NameFor(Request.IsHttps),
            session.Id.Value,
            SessionCookie.Carrying(Request.IsHttps, outcome.SessionLifetime));

        return Ok(BaseResponder<MeResponder>.Success(MeResponder.Of(session)));
    }

    private string Patience(DateTime retryAt)
    {
        TimeSpan left = retryAt - clock.GetUtcNow().UtcDateTime;
        long seconds = Math.Max(1, (long)Math.Ceiling(left.TotalSeconds));

        return seconds.ToString(CultureInfo.InvariantCulture);
    }

    private string Caller() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "an unnamed caller";
}
