namespace Carina.Api.Authentication;

public static class EndpointEffectExtensions
{
    public static TBuilder WithEffect<TBuilder>(this TBuilder builder, EndpointEffect effect)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpoint => endpoint.Metadata.Add(new EndpointEffectAttribute(effect)));

        return builder;
    }

    public static EndpointEffect? DeclaredEffect(this Endpoint? endpoint)
        => endpoint?.Metadata.GetMetadata<EndpointEffectAttribute>()?.Effect;
}
