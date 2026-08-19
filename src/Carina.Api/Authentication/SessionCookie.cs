namespace Carina.Api.Authentication;

public static class SessionCookie
{
    public const string PlainName = "carina_session";

    public const string HostName = "__Host-carina_session";

    public const string Path = "/";

    public static string NameFor(bool secure) => secure ? HostName : PlainName;

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
