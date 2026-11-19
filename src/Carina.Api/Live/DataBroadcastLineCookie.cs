namespace Carina.Api.Live;

public static class DataBroadcastLineCookie
{
    public const string Name = "vela-data-broadcast-network";

    public const string Allows = "allows";

    public static bool AllowsTheLine(IRequestCookieCollection cookies)
    {
        ArgumentNullException.ThrowIfNull(cookies);

        return cookies.TryGetValue(Name, out string? said) && string.Equals(said, Allows, StringComparison.Ordinal);
    }
}
