using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class OidcEndpointsTests
{
    private const string Issuer = "https://login.example.test";

    [Fact]
    public void ADiscoveryDocumentIsReadForTheFourPlacesAProviderIsReachedAt()
    {
        OidcEndpoints endpoints = OidcEndpoints.Of(
            Issuer,
            $"{Issuer}/authorize",
            $"{Issuer}/token",
            $"{Issuer}/jwks");

        Assert.Equal(Issuer, endpoints.Issuer);
        Assert.Equal(new Uri($"{Issuer}/authorize"), endpoints.Authorization);
        Assert.Equal(new Uri($"{Issuer}/token"), endpoints.Token);
        Assert.Equal(new Uri($"{Issuer}/jwks"), endpoints.Jwks);
    }

    [Theory]
    [InlineData("http://login.example.test/authorize")]
    [InlineData("/authorize")]
    [InlineData("javascript:alert(1)")]
    public void AProviderReachedOverAnythingButHttpsIsRefused(string authorization)
    {
        Assert.Throws<ArgumentException>(
            () => OidcEndpoints.Of(Issuer, authorization, $"{Issuer}/token", $"{Issuer}/jwks"));
    }

    [Fact]
    public void ADocumentThatNamesNoIssuerIsRefusedBecauseThereWouldBeNothingToCompareAgainst()
    {
        Assert.Throws<ArgumentException>(
            () => OidcEndpoints.Of("  ", $"{Issuer}/authorize", $"{Issuer}/token", $"{Issuer}/jwks"));
    }
}
