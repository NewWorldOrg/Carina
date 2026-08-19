namespace Carina.Api.Authentication;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class EndpointEffectAttribute(EndpointEffect effect) : Attribute
{
    public EndpointEffect Effect { get; } = effect;
}
