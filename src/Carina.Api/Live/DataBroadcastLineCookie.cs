namespace Carina.Api.Live;

/// <summary>
/// Whether the browser asking allows a data broadcast document to talk to the broadcaster's servers, as the web
/// frontend keeps it in a cookie of its own beside its other viewing settings. Without the cookie, or with any other
/// value in it, communication is off. The cookie is a preference of the browser, never a grant: who may ask is
/// still decided by the session.
/// </summary>
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
