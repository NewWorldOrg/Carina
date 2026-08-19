using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class PkceChallengeTests
{
    [Fact]
    public void TwoHandshakesNeverShareAVerifier()
    {
        Assert.NotEqual(PkceChallenge.Issue().Verifier, PkceChallenge.Issue().Verifier);
    }

    [Fact]
    public void TheChallengeIsTheDigestOfTheVerifierSoTheProviderCanTellThemApart()
    {
        PkceChallenge pkce = PkceChallenge.Issue();

        Assert.Equal(
            Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(pkce.Verifier))),
            pkce.Challenge);
    }

    [Fact]
    public void TheChallengeIsNotTheVerifierBecauseTheRedirectIsReadableAlongTheWay()
    {
        PkceChallenge pkce = PkceChallenge.Issue();

        Assert.NotEqual(pkce.Verifier, pkce.Challenge);
    }

    [Fact]
    public void TheOnlyMethodOfferedIsTheDigestOneBecausePlainDefeatsThePoint()
    {
        Assert.Equal("S256", PkceChallenge.Method);
    }

    [Fact]
    public void AnIssuedVerifierIsLongEnoughToBeWorthDigesting()
    {
        Assert.True(PkceChallenge.Issue().Verifier.Length >= PkceChallenge.ShortestVerifier);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("")]
    public void AVerifierTooShortToBeUnguessableIsRefused(string verifier)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PkceChallenge.From(verifier));
    }

    [Fact]
    public void AVerifierCarryingCharactersTheFormCannotHoldIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => PkceChallenge.From(new string('/', PkceChallenge.ShortestVerifier)));
    }

    [Fact]
    public void AVerifierCarriedBackFromTheStoreProducesTheSameChallenge()
    {
        PkceChallenge issued = PkceChallenge.Issue();

        Assert.Equal(issued.Challenge, PkceChallenge.From(issued.Verifier).Challenge);
    }
}
