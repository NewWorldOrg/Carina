namespace Carina.Api.Common;

/// <summary>
/// The pattern of the route a request matched, which is what a log line names in place of the path itself.
/// </summary>
public static class RouteShape
{
    public const string NoRoute = "(no route)";

    public static string Of(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { Length: > 0 } pattern }
            ? $"/{pattern.TrimStart('/')}"
            : NoRoute;
    }
}
