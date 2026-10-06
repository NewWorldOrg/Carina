using System.Security.Claims;

using Carina.Domain.Auth;

namespace Carina.Api.Authentication;

public static class SessionClaims
{
    public const string Session = "carina:session";

    public const string Method = "carina:method";

    public const string Device = "carina:device";

    public const string DisplayName = "carina:display-name";

    public static ClaimsPrincipal Principal(AuthSession session, string scheme)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, session.Subject.Value),
                new Claim(ClaimTypes.Name, session.Subject.Value),
                new Claim(Session, session.Handle.Value),
                new Claim(Method, session.Method.ToString()),
                new Claim(Device, session.DeviceLabel),
                new Claim(DisplayName, session.DisplayName),
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

    public static SessionHandle? SessionOf(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string? carried = principal.FindFirstValue(Session);

        return string.IsNullOrEmpty(carried) ? null : new SessionHandle(carried);
    }

    public static AuthMethod? MethodOf(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string? carried = principal.FindFirstValue(Method);

        return Enum.TryParse(carried, ignoreCase: false, out AuthMethod method) ? method : null;
    }

    /// <summary>
    /// The display name of whoever the session signed in as, or null for a principal no session made.
    /// </summary>
    public static string? DisplayNameOf(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string? carried = principal.FindFirstValue(DisplayName);

        return string.IsNullOrEmpty(carried) ? null : carried;
    }

    public static string? DeviceOf(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string? carried = principal.FindFirstValue(Device);

        return string.IsNullOrEmpty(carried) ? null : carried;
    }
}
