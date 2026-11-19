namespace Carina.Api.Live;

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
