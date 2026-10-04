using System.Text;

using Carina.Domain.Auth;

using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Authentication;

public static class PlaybackTicketCarrier
{
    public const string QueryKey = "ticket";

    private const string Bearer = "Bearer ";

    private const string Basic = "Basic ";

    private const char Separator = ':';

    /// <summary>The ticket a request carries on its Authorization header, or in its query when it has no such header.</summary>
    public static string? OfferedBy(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        StringValues header = request.Headers[HeaderNames.Authorization];
        string? carried = header.Count == 0 ? TheOnly(request.Query[QueryKey]) : OnTheHeader(header);

        return Unguessable.IsOne(carried) ? carried : null;
    }

    private static string? OnTheHeader(StringValues header)
        => TheOnly(header) is { } offered ? Carried(offered) : null;

    private static string? TheOnly(StringValues values) => values is [string one] ? one : null;

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
