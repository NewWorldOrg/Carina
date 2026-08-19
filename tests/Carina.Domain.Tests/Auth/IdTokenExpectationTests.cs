using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class IdTokenExpectationTests
{
    private const string Issuer = "https://login.example.test";

    private static readonly DateTime Now = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    private static readonly string Nonce = Unguessable.Issue();

    private static readonly IdTokenExpectation Expected = new(Issuer, "carina", Nonce);

    [Fact]
    public void ATokenThatAnswersEveryPointIsAccepted()
    {
        Assert.Equal(OidcRefusal.None, Refuses(Claims()));
    }

    [Fact]
    public void ATokenFromAnotherIssuerIsRefused()
    {
        Assert.Equal(
            OidcRefusal.TheIssuerIsNotTheOneConfigured,
            Refuses(Claims() with { Issuer = "https://login.elsewhere.test" }));
    }

    [Fact]
    public void ATokenIssuedForAnotherClientIsRefused()
    {
        Assert.Equal(
            OidcRefusal.TheTokenWasIssuedForSomebodyElse,
            Refuses(Claims() with { Audiences = ["somebody-else"] }));
    }

    [Fact]
    public void ATokenNamingSeveralAudiencesIsAcceptedWhenOneOfThemIsUs()
    {
        Assert.Equal(OidcRefusal.None, Refuses(Claims() with { Audiences = ["somebody-else", "carina"] }));
    }

    [Fact]
    public void AnExpiredTokenIsRefused()
    {
        Assert.Equal(
            OidcRefusal.TheIdTokenExpired,
            Refuses(Claims() with { ExpiresAt = Now.AddMinutes(-3) }));
    }

    [Fact]
    public void ClocksDriftSoATokenJustPastItsExpiryIsStillAccepted()
    {
        Assert.Equal(
            OidcRefusal.None,
            Refuses(Claims() with { ExpiresAt = Now.AddMinutes(-1) }));
    }

    [Fact]
    public void ATokenCarryingSomebodyElsesNonceIsRefusedBecauseThatIsAReplay()
    {
        Assert.Equal(
            OidcRefusal.TheNonceIsNotTheOneIssued,
            Refuses(Claims() with { Nonce = Unguessable.Issue() }));
    }

    [Fact]
    public void ATokenCarryingNoNonceAtAllIsRefused()
    {
        Assert.Equal(OidcRefusal.TheNonceIsNotTheOneIssued, Refuses(Claims() with { Nonce = null }));
    }

    [Fact]
    public void TheIssuerIsComparedExactlyBecauseATrailingSlashMakesADifferentProvider()
    {
        Assert.Equal(
            OidcRefusal.TheIssuerIsNotTheOneConfigured,
            Refuses(Claims() with { Issuer = $"{Issuer}/" }));
    }

    private static OidcRefusal Refuses(OidcClaims claims)
        => Expected.Refuses(claims, Now, OidcLoginPolicy.Default.ClockSkew);

    private static OidcClaims Claims()
        => new()
        {
            Issuer = Issuer,
            Audiences = ["carina"],
            Subject = "owner",
            ExpiresAt = Now.AddMinutes(5),
            Nonce = Nonce,
        };
}
