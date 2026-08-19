using System.Text.RegularExpressions;

using Carina.Api.Authentication;
using Carina.Api.Tests.Unit;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

internal static partial class RouteInventory
{
    public static IReadOnlyList<RoutedSurface> Of(WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        EndpointDataSource endpoints = factory.Services.GetRequiredService<EndpointDataSource>();

        return [.. endpoints.Endpoints.OfType<RouteEndpoint>().SelectMany(Surfaces)];
    }

    public static string SamplePath(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        return Parameter().Replace(pattern, match => Sample(match.Groups[1].Value));
    }

    [GeneratedRegex(@"\{\*?([^{}]+)\}")]
    private static partial Regex Parameter();

    private static IEnumerable<RoutedSurface> Surfaces(RouteEndpoint endpoint)
    {
        EndpointEffect? effect = endpoint.DeclaredEffect();
        string pattern = $"/{endpoint.RoutePattern.RawText?.TrimStart('/')}";
        IReadOnlyList<string> methods =
            endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];

        return methods.Count == 0
            ? [new RoutedSurface(HttpMethods.Get, pattern, effect)]
            : methods.Select(method => new RoutedSurface(method, pattern, effect));
    }

    private static string Sample(string parameter)
    {
        string[] parts = parameter.Split(':');

        return parts.Length > 1 ? Sampled(parts[1]) : "sample";
    }

    private static string Sampled(string constraint)
        => constraint switch
        {
            "guid" => Guid.Empty.ToString(),
            "int" or "long" => "1",
            _ => "sample",
        };
}
