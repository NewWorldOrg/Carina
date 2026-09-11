using System.Text.Encodings.Web;

using Carina.Domain.Auth;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Carina.Api.Authentication;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggers,
    UrlEncoder encoder,
    IAuthSessionRepository sessions,
    SessionPolicy policy,
    TimeProvider clock) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggers, encoder)
{
    public const string SchemeName = "CarinaSession";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(SessionCookie.Name, out string? carried)
            || string.IsNullOrEmpty(carried))
        {
            return AuthenticateResult.NoResult();
        }

        AuthSession? session = Named(carried) is { } id
            ? await sessions.FindAsync(id, Context.RequestAborted)
            : null;

        DateTime now = clock.GetUtcNow().UtcDateTime;

        if (session is null || session.StatusAt(now, policy) is not SessionStatus.Active)
        {
            Response.Cookies.Delete(SessionCookie.Name, SessionCookie.Discarding(Request.IsHttps));

            return AuthenticateResult.NoResult();
        }

        if (session.Touch(now, policy))
        {
            await sessions.SaveAsync(session, Context.RequestAborted);
        }

        return AuthenticateResult.Success(
            new AuthenticationTicket(SessionClaims.Principal(session, Scheme.Name), Scheme.Name));
    }

    private static SessionId? Named(string carried)
    {
        try
        {
            return new SessionId(carried);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
