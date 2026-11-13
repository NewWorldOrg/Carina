using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Auth;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.Feature.Live;

[Trait("Category", "Feature")]
public sealed class IssueLiveTicketActionTests
{
    [Fact]
    public async Task ATicketIsIssuedForAChannelThatCanBeWatchedAndNamesThatChannelAlone()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using HttpResponseMessage response = await feature.AskForATicketAsync(new { networkId = 32736, serviceId = 1024 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement data = body.RootElement.GetProperty("data");

        Assert.False(string.IsNullOrEmpty(data.GetProperty("inTheClear").GetString()));
        Assert.Equal(["inTheClear", "lapsesAt"], data.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        (Subject subject, PlaybackTarget target) = Assert.Single(feature.Tickets.Issued);

        Assert.Equal(new Subject(TestAuthenticationHandler.Tester), subject);
        Assert.Equal(PlaybackTarget.LiveChannel("32736-1024"), target);
        Assert.Equal(PlaybackTargetKind.LiveChannel, target.Kind);
        Assert.NotEqual(PlaybackTarget.Recording("32736-1024"), target);
    }

    [Theory]
    [InlineData(32736, 1040)]
    [InlineData(32736, 9999)]
    [InlineData(32737, 1024)]
    public async Task AChannelThatCannotBeWatchedGetsNoTicket(int network, int service)
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);
        feature.Seed(1040, "Unselected", remoteControlKey: 2, selected: false);

        using HttpResponseMessage response = await feature.AskForATicketAsync(new { networkId = network, serviceId = service });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(feature.Tickets.Issued);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"networkId\":32736}")]
    [InlineData("{\"networkId\":-1,\"serviceId\":1024}")]
    [InlineData("{\"networkId\":32736,\"serviceId\":65536}")]
    [InlineData("{\"networkId\":\"thirty\",\"serviceId\":1024}")]
    public async Task ATicketAskedForWithoutAWholeChannelIsRefused(string body)
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using StringContent content = new(body, System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await feature.Client.PostAsync(new Uri("/api/live/ticket", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(feature.Tickets.Issued);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data")]
    public async Task ATicketAskedForInAnythingButJsonIsRefusedAsUnsupportedMedia(string mediaType)
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);

        using HttpResponseMessage response = await feature.AskForATicketAsync(new { networkId = 32736, serviceId = 1024 }, mediaType);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(feature.Tickets.Issued);
    }

    [Fact]
    public async Task TooManyOutstandingTicketsIsSaidAsTooManyRequests()
    {
        await using LiveFeature feature = new();

        feature.Seed(1024, "Alpha", remoteControlKey: 1);
        feature.Tickets.Full = true;

        using HttpResponseMessage response = await feature.AskForATicketAsync(new { networkId = 32736, serviceId = 1024 });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

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
