using Carina.Domain.Auth;

namespace Carina.Api.Authentication;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class KeyedSurfaceAttribute : Attribute;

public static class KeyedSurfaces
{
    public const string InThePath = "key";

    public static TBuilder OpenedByItsKey<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpoint => endpoint.Metadata.Add(new KeyedSurfaceAttribute()));

        return builder;
    }

    public static bool IsOpenedByItsKey(this Endpoint? endpoint)
        => endpoint?.Metadata.GetMetadata<KeyedSurfaceAttribute>() is not null;

    public static string? OfferedBy(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.RouteValues.TryGetValue(InThePath, out object? placed) && placed is string key && Unguessable.IsOne(key)
            ? key
            : null;
    }
}
