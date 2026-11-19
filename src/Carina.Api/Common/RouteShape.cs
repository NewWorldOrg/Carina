namespace Carina.Api.Common;

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
