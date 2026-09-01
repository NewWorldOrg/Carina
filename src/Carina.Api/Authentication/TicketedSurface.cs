namespace Carina.Api.Authentication;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class TicketedSurfaceAttribute : Attribute;

public static class TicketedSurfaces
{
    public static TBuilder Ticketed<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpoint => endpoint.Metadata.Add(new TicketedSurfaceAttribute()));

        return builder;
    }

    public static bool IsTicketed(this Endpoint? endpoint)
        => endpoint?.Metadata.GetMetadata<TicketedSurfaceAttribute>() is not null;
}
