namespace Carina.Api.Live;

public static class DataBroadcastHybridcastCookie
{
    public const string Name = "vela-data-broadcast-hybridcast";

    public const string Allows = "allows";

    public static bool AllowsHybridcast(IRequestCookieCollection cookies)
    {
        ArgumentNullException.ThrowIfNull(cookies);

        return cookies.TryGetValue(Name, out string? said) && string.Equals(said, Allows, StringComparison.Ordinal);
    }
}
