using Carina.Api.Authentication;

namespace Carina.Api.Tests.Unit;

public sealed record RoutedSurface(string Method, string Pattern, EndpointEffect? Effect)
{
    public override string ToString() => $"{Method} {Pattern}";
}
