using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class OidcClaimsTests
{
    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BrAu018AnIdentityIsShownByItsEmailWhenTheProviderIssuedOne()
    {
        OidcClaims claims = Claims() with { Email = "alice@example.test", Name = "Alice" };

        Assert.Equal("alice@example.test", claims.DisplayName);
    }

    [Fact]
    public void BrAu018AnIdentityWithoutAnEmailIsShownByItsName()
    {
        OidcClaims claims = Claims() with { Name = "Alice" };

        Assert.Equal("Alice", claims.DisplayName);
    }

    [Fact]
    public void BrAu018AnIdentityTheProviderNamedNoOtherWayIsShownByItsSubject()
    {
        Assert.Equal("108204329581372", Claims().DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BrAu018ABlankClaimIsNoNameAndTheNextOneIsUsedInstead(string blank)
    {
        OidcClaims claims = Claims() with { Email = blank, Name = "Alice" };

        Assert.Equal("Alice", claims.DisplayName);
        Assert.Equal("108204329581372", (Claims() with { Email = blank, Name = blank }).DisplayName);
    }

    private static OidcClaims Claims() => new()
    {
        Issuer = "https://login.example.test",
        Audiences = ["carina"],
        Subject = "108204329581372",
        ExpiresAt = At,
    };
}
