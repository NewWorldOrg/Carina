using System.Text;

using Carina.Domain.Auth;

using Microsoft.Net.Http.Headers;

namespace Carina.Api.Authentication;

public static class PlaybackTicketCarrier
{
    public const string TheUser = "ticket";

    private const string Bearer = "Bearer ";

    private const string Basic = "Basic ";

    private const char Separator = ':';

    public static string? OfferedBy(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Cookies.ContainsKey(SessionCookie.Name))
        {
            return null;
        }

        if (request.Headers[HeaderNames.Authorization] is not [string offered])
        {
            return null;
        }

        string? carried = Carried(offered);

        return Unguessable.IsOne(carried) ? carried : null;
    }

    private static string? Carried(string offered)
    {
        if (offered.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase))
        {
            return offered[Bearer.Length..];
        }

        return offered.StartsWith(Basic, StringComparison.OrdinalIgnoreCase)
            ? Password(offered[Basic.Length..])
            : null;
    }

    private static string? Password(string credentials)
    {
        byte[] decoded = new byte[credentials.Length];

        if (!Convert.TryFromBase64String(credentials, decoded, out int written))
        {
            return null;
        }

        string pair = Encoding.UTF8.GetString(decoded, 0, written);
        int separator = pair.IndexOf(Separator);

        return separator < 0 ? null : pair[(separator + 1)..];
    }
}
