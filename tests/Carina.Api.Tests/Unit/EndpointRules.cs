using Carina.Api.Authentication;

using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.Unit;

public static class EndpointRules
{
    public static IReadOnlyList<string> SurfacesWithoutADeclaredEffect(
        IEnumerable<RoutedSurface> surfaces)
    {
        ArgumentNullException.ThrowIfNull(surfaces);

        return
        [
            .. surfaces
                .Where(surface => surface.Effect is null)
                .Select(surface => surface.ToString())
                .Order(StringComparer.Ordinal),
        ];
    }

    public static IReadOnlyList<string> SurfacesChangingStateWithoutAskingForIt(
        IEnumerable<RoutedSurface> surfaces)
    {
        ArgumentNullException.ThrowIfNull(surfaces);

        return
        [
            .. surfaces
                .Where(surface => Reads(surface.Method))
                .Where(surface => surface.Effect is EndpointEffect.Changing or EndpointEffect.Destructive)
                .Select(surface => surface.ToString())
                .Order(StringComparer.Ordinal),
        ];
    }

    private static bool Reads(string method)
        => HttpMethods.IsGet(method)
           || HttpMethods.IsHead(method)
           || HttpMethods.IsOptions(method)
           || HttpMethods.IsTrace(method);
}
