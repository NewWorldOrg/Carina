namespace Carina.Api.Live;

/// <summary>
/// Whether the browser asking lets a data broadcast document be given the receiver's identifier, the ID of the
/// conditional-access card, as the web frontend keeps it in a cookie of its own beside communication. Without the
/// cookie, or with any other value in it, the identifier is kept back. Like communication, it is a preference of the
/// browser, never a grant: who may ask is still decided by the session.
/// </summary>
public static class DataBroadcastReceiverIdCookie
{
    public const string Name = "vela-data-broadcast-receiver-id";

    public const string Hands = "hands";

    public static bool HandsTheId(IRequestCookieCollection cookies)
    {
        ArgumentNullException.ThrowIfNull(cookies);

        return cookies.TryGetValue(Name, out string? said) && string.Equals(said, Hands, StringComparison.Ordinal);
    }
}
