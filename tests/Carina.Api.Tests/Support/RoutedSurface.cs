using Carina.Api.Authentication;

namespace Carina.Api.Tests.Support;

public sealed record RoutedSurface(string Method, string Pattern, EndpointEffect? Effect, bool OpenToTheFrame = false)
{
    public override string ToString() => $"{Method} {Pattern}";
}
