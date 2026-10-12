namespace Carina.Api.Live;

/// <summary>
/// Whether the browser asking lets a data broadcast document start a Hybridcast application, as the web frontend
/// keeps it in a cookie of its own beside communication. Without the cookie, or with any other value in it,
/// Hybridcast is off. Like communication, it is a preference of the browser, never a grant: who may ask is still
/// decided by the session.
/// </summary>
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
