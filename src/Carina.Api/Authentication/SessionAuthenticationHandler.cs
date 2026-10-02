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
    SignInRecord record,
    TimeProvider clock) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggers, encoder)
{
    public const string SchemeName = "CarinaSession";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (SessionCookie.CarriedBy(Request) is not { } carried)
        {
            return AuthenticateResult.NoResult();
        }

        AuthSession? session = Named(carried) is { } id
            ? await sessions.FindAsync(SessionHandle.Of(id), Context.RequestAborted)
            : null;

        DateTime now = clock.GetUtcNow().UtcDateTime;

        if (session is null || session.StatusAt(now, policy) is not SessionStatus.Active)
        {
            Response.Cookies.Delete(SessionCookie.Name, SessionCookie.Discarding(Request.IsHttps));
            NeverStored.Mark(Response);
            record.Write(Context, WhyItOpenedNothing(session), session?.Method, session?.DeviceLabel);

            return AuthenticateResult.NoResult();
        }

        if (session.Touch(now, policy))
        {
            await sessions.SaveAsync(session, Context.RequestAborted);
            record.Write(Context, SignInMoment.TheSessionWasUsed, session.Method, session.DeviceLabel);
        }

        return AuthenticateResult.Success(
            new AuthenticationTicket(SessionClaims.Principal(session, Scheme.Name), Scheme.Name));
    }

    private static SignInMoment WhyItOpenedNothing(AuthSession? session)
    {
        if (session is null)
        {
            return SignInMoment.TheSessionCookieNamedNoSession;
        }

        return session.RevokedAt is null
            ? SignInMoment.TheSessionHadExpired
            : SignInMoment.TheSessionHadBeenRevoked;
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
