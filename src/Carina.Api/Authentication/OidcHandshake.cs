using Carina.Domain.Auth;

namespace Carina.Api.Authentication;

public static class OidcHandshake
{
    public const string StartRoute = "api/auth/oidc/start";

    public const string CallbackRoute = "api/auth/oidc/callback";

    public const string ConfigRoute = "api/auth/oidc-config";

    public const string StartPath = $"/{StartRoute}";

    public const string CallbackPath = $"/{CallbackRoute}";

    public const string MarkName = "carina_oidc";

    public const string StateKey = "state";

    public const string CodeKey = "code";

    public static string ArrivedAt(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return $"{request.Scheme}://{request.Host.Value}";
    }

    public static string? MarkCarriedBy(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Cookies.TryGetValue(MarkName, out string? carried)
               && Unguessable.IsOne(carried)
            ? carried
            : null;
    }

    public static CookieOptions MarkCookie(bool secure, TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        return new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = SessionCookie.Path,
            MaxAge = lifetime,
        };
    }
}
