using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.DataBroadcast;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Carina.Api.Tests.Feature.Live;

[Trait("Category", "Feature")]
public sealed class GetLiveDataBroadcastAitActionTests
{
    [Theory(DisplayName = "BR-BD-011: an AIT is read only where the browser allows both communication and Hybridcast")]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AnAitIsReadOnlyWhereBothAreAllowed(bool line, bool hybridcast)
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow(line, hybridcast);

        using HttpResponseMessage response = await feature.Viewer.GetAsync(new Uri($"{HybridcastRelayFeature.Ait}?uri={Uri.EscapeDataString(HybridcastRelayFeature.AitAddress)}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(feature.Relay.Asked);
    }

    [Fact(DisplayName = "FR-015: an AIT is read for the channel in the path and answered with the applications it names")]
    public async Task AnAitIsAnsweredWithItsApplications()
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow();

        using HttpResponseMessage response = await feature.Viewer.GetAsync(new Uri($"{HybridcastRelayFeature.Ait}?uri={Uri.EscapeDataString(HybridcastRelayFeature.AitAddress)}", UriKind.Relative));
        (HttpStatusCode status, JsonElement body) = await DataBroadcastRelayFeature.ReadAsync(response);
        JsonElement application = Assert.Single(body.GetProperty("data").GetProperty("applications").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(16, application.GetProperty("orgId").GetInt64());
        Assert.Equal(1, application.GetProperty("appId").GetInt32());
        Assert.Equal(1, application.GetProperty("controlCode").GetInt32());
        Assert.Equal(HybridcastRelayFeature.Entry, application.GetProperty("entryUrl").GetString());
        Assert.Equal(1, application.GetProperty("profile").GetInt32());
        JsonElement pair = Assert.Single(application.GetProperty("boundary").EnumerateArray());
        Assert.Equal(0x1fff, pair.GetProperty("permissionBitmap").GetInt32());
        Assert.Equal(["http://station.example.jp", "ws://station.example.jp"], pair.GetProperty("managedUrls").EnumerateArray().Select(url => url.GetString()));
        Assert.Equal($"ait 32736-1024 {HybridcastRelayFeature.AitAddress}", Assert.Single(feature.Relay.Asked));
    }

    [Fact(DisplayName = "BR-BD-013: an answer that is not an AIT is 502 with the result and the status the server answered")]
    public async Task AnAnswerThatIsNotAnAitIs502()
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow();
        feature.Relay.Ait = new RelayedAit(RelayResult.Answered, 404, null);

        using HttpResponseMessage response = await feature.Viewer.GetAsync(new Uri($"{HybridcastRelayFeature.Ait}?uri={Uri.EscapeDataString(HybridcastRelayFeature.AitAddress)}", UriKind.Relative));
        (HttpStatusCode status, JsonElement body) = await DataBroadcastRelayFeature.ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("result").GetInt32());
        Assert.Equal(404, body.GetProperty("data").GetProperty("httpStatus").GetInt32());
    }

    [Theory(DisplayName = "an AIT asked for without its address, or for a channel that cannot be watched live here, is refused and read from nowhere")]
    [InlineData("/api/live/32736-1024/data-broadcast/ait", HttpStatusCode.BadRequest)]
    [InlineData("/api/live/32736-1026/data-broadcast/ait?uri=http%3A%2F%2Fhc.station.example.jp%2Fapp.ait", HttpStatusCode.NotFound)]
    public async Task AnAitAskedForBadlyIsRefused(string path, HttpStatusCode expected)
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow();

        using HttpResponseMessage response = await feature.Viewer.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(feature.Relay.Asked);
    }
}
