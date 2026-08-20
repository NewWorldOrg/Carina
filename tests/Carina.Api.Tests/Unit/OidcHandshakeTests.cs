using Carina.Api.Authentication;
using Carina.Domain.Auth;

using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.Unit;

public sealed class OidcHandshakeTests
{
    private static readonly string Mark = Unguessable.Issue();

    private static readonly string AnotherMark = Unguessable.Issue();

    [Fact]
    public void OverHttpsTheMarkTakesThePrefixThatTiesItToTheHostAndThePath()
    {
        Assert.Equal("__Host-carina_oidc", OidcHandshake.MarkNameFor(secure: true));
    }

    [Fact]
    public void OverPlainHttpTheMarkDropsThePrefixBecauseABrowserWouldRefuseItWithoutSecure()
    {
        Assert.Equal("carina_oidc", OidcHandshake.MarkNameFor(secure: false));
    }

    [Fact]
    public void ThePrefixedSpellingIsReadEvenWhereTheRequestItselfArrivedOverPlainHttp()
    {
        HttpRequest request = Carrying(false, $"{OidcHandshake.HostMarkName}={Mark}");

        Assert.Equal(Mark, OidcHandshake.MarkCarriedBy(request));
    }

    [Fact]
    public void TheUnprefixedSpellingIsReadEvenWhereTheRequestItselfArrivedOverHttps()
    {
        HttpRequest request = Carrying(true, $"{OidcHandshake.PlainMarkName}={Mark}");

        Assert.Equal(Mark, OidcHandshake.MarkCarriedBy(request));
    }

    [Fact]
    public void WithBothSpellingsInHandThePrefixedOneWinsBecauseOnlyTheHostItselfCouldHaveSetIt()
    {
        HttpRequest request = Carrying(
            false,
            $"{OidcHandshake.PlainMarkName}={AnotherMark}",
            $"{OidcHandshake.HostMarkName}={Mark}");

        Assert.Equal(Mark, OidcHandshake.MarkCarriedBy(request));
    }

    [Fact]
    public void AValueNobodyCouldHaveIssuedIsPassedOverForTheOneThatLooksLikeAMark()
    {
        HttpRequest request = Carrying(
            true,
            $"{OidcHandshake.HostMarkName}=not-a-mark",
            $"{OidcHandshake.PlainMarkName}={Mark}");

        Assert.Equal(Mark, OidcHandshake.MarkCarriedBy(request));
    }

    [Fact]
    public void ARequestCarryingNeitherSpellingCarriesNoMarkAtAll()
    {
        Assert.Null(OidcHandshake.MarkCarriedBy(Carrying(true, "something-else=whatever")));
    }

    private static HttpRequest Carrying(bool secure, params string[] cookies)
    {
        var context = new DefaultHttpContext();

        context.Request.IsHttps = secure;
        context.Request.Headers.Cookie = string.Join("; ", cookies);

        return context.Request;
    }
}
