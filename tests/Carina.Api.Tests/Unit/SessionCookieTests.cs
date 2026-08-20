using Carina.Api.Authentication;

using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.Unit;

public sealed class SessionCookieTests
{
    [Fact]
    public void OverHttpsTheCookieTakesThePrefixThatTiesItToTheHostAndThePath()
    {
        Assert.Equal("__Host-carina_session", SessionCookie.NameFor(secure: true));
    }

    [Fact]
    public void OverPlainHttpTheCookieDropsThePrefixBecauseABrowserWouldRefuseItWithoutSecure()
    {
        Assert.Equal("carina_session", SessionCookie.NameFor(secure: false));
    }

    [Fact]
    public void ThePrefixedSpellingIsReadEvenWhereTheRequestItselfArrivedOverPlainHttp()
    {
        HttpRequest request = Carrying(false, $"{SessionCookie.HostName}=a-session-id");

        Assert.Equal("a-session-id", SessionCookie.CarriedBy(request));
    }

    [Fact]
    public void TheUnprefixedSpellingIsReadEvenWhereTheRequestItselfArrivedOverHttps()
    {
        HttpRequest request = Carrying(true, $"{SessionCookie.PlainName}=a-session-id");

        Assert.Equal("a-session-id", SessionCookie.CarriedBy(request));
    }

    [Fact]
    public void WithBothSpellingsInHandThePrefixedOneWinsBecauseOnlyTheHostItselfCouldHaveSetIt()
    {
        HttpRequest request = Carrying(
            false,
            $"{SessionCookie.PlainName}=the-weaker-one",
            $"{SessionCookie.HostName}=the-vouched-one");

        Assert.Equal("the-vouched-one", SessionCookie.CarriedBy(request));
    }

    [Fact]
    public void AnEmptyPrefixedCookieDoesNotStandInForTheOneThatCarriesASession()
    {
        HttpRequest request = Carrying(
            true,
            $"{SessionCookie.HostName}=",
            $"{SessionCookie.PlainName}=a-session-id");

        Assert.Equal("a-session-id", SessionCookie.CarriedBy(request));
    }

    [Fact]
    public void ARequestCarryingNeitherSpellingCarriesNoSessionAtAll()
    {
        Assert.Null(SessionCookie.CarriedBy(Carrying(true, "something-else=whatever")));
    }

    [Fact]
    public void TheCookieIsUnreadableToScriptAndUnsentAcrossSitesAndCoversTheWholeApp()
    {
        CookieOptions options = SessionCookie.Carrying(secure: true, TimeSpan.FromDays(30));

        Assert.True(options.HttpOnly);
        Assert.True(options.Secure);
        Assert.Equal(SameSiteMode.Lax, options.SameSite);
        Assert.Equal("/", options.Path);
        Assert.Null(options.Domain);
        Assert.Equal(TimeSpan.FromDays(30), options.MaxAge);
    }

    [Fact]
    public void TheHostPrefixRulesAreAllMetSoABrowserAcceptsTheCookieItNames()
    {
        CookieOptions options = SessionCookie.Carrying(secure: true, TimeSpan.FromDays(30));

        Assert.StartsWith("__Host-", SessionCookie.NameFor(secure: true), StringComparison.Ordinal);
        Assert.True(options.Secure);
        Assert.Equal("/", options.Path);
        Assert.Null(options.Domain);
    }

    [Fact]
    public void TheDiscardedCookieCarriesTheSameAttributesSoTheBrowserMatchesTheOneItHolds()
    {
        CookieOptions options = SessionCookie.Discarding(secure: true);

        Assert.True(options.HttpOnly);
        Assert.True(options.Secure);
        Assert.Equal(SameSiteMode.Lax, options.SameSite);
        Assert.Equal("/", options.Path);
    }

    [Fact]
    public void ACookieWithNoLifeLeftIsRefusedRatherThanHandedOut()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionCookie.Carrying(secure: true, TimeSpan.Zero));
    }

    private static HttpRequest Carrying(bool secure, params string[] cookies)
    {
        var context = new DefaultHttpContext();

        context.Request.IsHttps = secure;
        context.Request.Headers.Cookie = string.Join("; ", cookies);

        return context.Request;
    }
}
