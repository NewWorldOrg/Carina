using Carina.Api.Authentication;

using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.Unit;

public sealed class SessionCookiePolicyTests
{
    [Fact]
    public void NoCookieLeavesWithAWeakerSameSiteThanLax()
    {
        Assert.Equal(SameSiteMode.Lax, SessionCookiePolicy.Options.MinimumSameSitePolicy);
    }

    [Fact]
    public void NoCookieIsReadableByAScript()
    {
        Assert.Equal(HttpOnlyPolicy.Always, SessionCookiePolicy.Options.HttpOnly);
    }

    [Fact]
    public void ACookieIsMarkedSecureWhereverTheRequestArrivedOverHttps()
    {
        Assert.Equal(CookieSecurePolicy.SameAsRequest, SessionCookiePolicy.Options.Secure);
    }
}
