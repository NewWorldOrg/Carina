namespace Carina.Api.Authentication;

public static class PageRequest
{
    private const string Screen = "text/html";

    public static bool ExpectsAScreen(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
               && !request.Path.StartsWithSegments("/api")
               && NamesHtml(request);
    }

    private static bool NamesHtml(HttpRequest request)
        => request.Headers.Accept.Any(accepted =>
            accepted?.Contains(Screen, StringComparison.OrdinalIgnoreCase) is true);
}
