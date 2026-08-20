using Carina.Api.Authentication;

using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.Unit;

public sealed class SessionCookieTests
{
    [Fact]
    public void TheCookieAnswersToOneNameSoTheSameOneIsFoundHoweverTheRequestArrived()
    {
        Assert.Equal("carina_session", SessionCookie.Name);
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
    public void OverPlainHttpTheCookieGivesUpTheSecureFlagAndNothingElse()
    {
        CookieOptions options = SessionCookie.Carrying(secure: false, TimeSpan.FromDays(30));

        Assert.False(options.Secure);
        Assert.True(options.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, options.SameSite);
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
        Assert.False(SessionCookie.Discarding(secure: false).Secure);
    }

    [Fact]
    public void ACookieWithNoLifeLeftIsRefusedRatherThanHandedOut()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionCookie.Carrying(secure: true, TimeSpan.Zero));
    }
}
