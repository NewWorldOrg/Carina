using Microsoft.Net.Http.Headers;

namespace Carina.Api.Authentication;

public static class RequestOrigin
{
    public static bool NamesThisOne(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Headers.Origin is [string named]
               && string.Equals(named, Here(request), StringComparison.OrdinalIgnoreCase);
    }

    public static bool NamesSomewhereElse(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Headers[HeaderNames.Origin].Count > 0 && !NamesThisOne(request);
    }

    private static string Here(HttpRequest request) => $"{request.Scheme}://{request.Host.Value}";
}
