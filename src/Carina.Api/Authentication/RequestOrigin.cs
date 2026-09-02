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

    private static string Here(HttpRequest request) => $"{AsThePageSaysIt(request.Scheme)}://{request.Host.Value}";

    private static string AsThePageSaysIt(string scheme)
    {
        if (string.Equals(scheme, Uri.UriSchemeWss, StringComparison.OrdinalIgnoreCase))
        {
            return Uri.UriSchemeHttps;
        }

        return string.Equals(scheme, Uri.UriSchemeWs, StringComparison.OrdinalIgnoreCase) ? Uri.UriSchemeHttp : scheme;
    }
}
