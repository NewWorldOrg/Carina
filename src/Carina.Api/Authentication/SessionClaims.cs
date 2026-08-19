using System.Security.Claims;

using Carina.Domain.Auth;

namespace Carina.Api.Authentication;

public static class SessionClaims
{
    public const string Session = "carina:session";

    public const string Method = "carina:method";

    public static ClaimsPrincipal Principal(AuthSession session, string scheme)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, session.Subject.Value),
                new Claim(ClaimTypes.Name, session.Subject.Value),
                new Claim(Session, session.Id.Value),
                new Claim(Method, session.Method.ToString()),
            ],
            scheme);

        return new ClaimsPrincipal(identity);
    }

    public static Subject? SubjectOf(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string? carried = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return string.IsNullOrEmpty(carried) ? null : new Subject(carried);
    }

    public static SessionId? SessionOf(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string? carried = principal.FindFirstValue(Session);

        return string.IsNullOrEmpty(carried) ? null : new SessionId(carried);
    }

    public static AuthMethod? MethodOf(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string? carried = principal.FindFirstValue(Method);

        return Enum.TryParse(carried, ignoreCase: false, out AuthMethod method) ? method : null;
    }
}
