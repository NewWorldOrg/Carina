using Carina.Domain.Auth;

namespace Carina.Api.Authentication;

/// <summary>
/// Marks a surface a player opens with the key a playlist handed it, which carries neither a session nor a ticket.
/// </summary>
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

    /// <summary>
    /// The key in the path when it has the shape of one. Whether it is one held is for the surface to judge.
    /// </summary>
    public static string? OfferedBy(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.RouteValues.TryGetValue(InThePath, out object? placed) && placed is string key && Unguessable.IsOne(key)
            ? key
            : null;
    }
}
