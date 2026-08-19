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
}
