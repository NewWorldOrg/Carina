namespace Carina.Api.Authentication;

public static class SessionCookie
{
    public const string Name = "carina_session";

    public const string Path = "/";

    public static string? CarriedBy(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Cookies.TryGetValue(Name, out string? carried) && !string.IsNullOrEmpty(carried)
            ? carried
            : null;
    }

    public static CookieOptions Carrying(bool secure, TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        return new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = Path,
            MaxAge = lifetime,
        };
    }

    public static CookieOptions Discarding(bool secure) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Lax,
        Path = Path,
    };
}
