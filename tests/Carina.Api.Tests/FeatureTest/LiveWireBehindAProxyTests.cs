using System.Net;

namespace Carina.Api.Tests.FeatureTest;

public sealed class LiveWireBehindAProxyTests
{
    [Fact]
    public async Task ARequestThatChangesStateBehindTheSameProxyIsStillAnsweredForThePageItNames()
    {
        await using ProxiedKestrelProbe probe = ProxiedKestrelProbe.ListeningOnKestrel();
        string cookie = await probe.SignedInCookieAsync();

        using HttpResponseMessage fromThisPage = await probe.PostThroughTheProxyAsync(
            "https",
            probe.PageAt(Uri.UriSchemeHttps),
            cookie);
        using HttpResponseMessage fromElsewhere = await probe.PostThroughTheProxyAsync(
            "https",
            "https://elsewhere.example",
            cookie);

        Assert.NotEqual(HttpStatusCode.Forbidden, fromThisPage.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, fromElsewhere.StatusCode);
    }
}
