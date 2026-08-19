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
        if (!Request.Cookies.TryGetValue(SessionCookie.NameFor(Request.IsHttps), out string? carried)
            || string.IsNullOrEmpty(carried))
        {
            return AuthenticateResult.NoResult();
        }

        SessionId id;

        try
        {
            id = new SessionId(carried);
        }
        catch (ArgumentException)
        {
            return AuthenticateResult.NoResult();
        }

        AuthSession? session = await sessions.FindAsync(id, Context.RequestAborted);

        if (session is null)
        {
            return AuthenticateResult.NoResult();
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;

        if (session.StatusAt(now, policy) is not SessionStatus.Active)
        {
            return AuthenticateResult.NoResult();
        }

        if (session.Touch(now, policy))
        {
            await sessions.SaveAsync(session, Context.RequestAborted);
        }

        return AuthenticateResult.Success(
            new AuthenticationTicket(SessionClaims.Principal(session, Scheme.Name), Scheme.Name));
    }
}
