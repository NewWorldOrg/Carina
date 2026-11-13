using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.DataBroadcast;

namespace Carina.Api.Tests.Feature.Live;

[Trait("Category", "Feature")]
public sealed class IssueLiveHybridcastTicketActionTests
{
    [Fact(DisplayName = "BR-BV-007: starting an application reads the AIT again and hands back a ticket and the path the frame opens")]
    public async Task StartingAnApplicationHandsBackATicket()
    {
        await using HybridcastRelayFeature feature = new();

        JsonElement started = await feature.StartedAsync();
        string ticket = started.GetProperty("inTheClear").GetString()!;

        Assert.Equal(43, ticket.Length);
        Assert.Equal(HybridcastRelayFeature.Entry, started.GetProperty("entryUrl").GetString());
        Assert.Equal(
            $"/api/live/32736-1024/data-broadcast/app/{ticket}?uri={Uri.EscapeDataString(HybridcastRelayFeature.Entry)}",
            started.GetProperty("address").GetString());
        Assert.Equal($"ait 32736-1024 {HybridcastRelayFeature.AitAddress}", Assert.Single(feature.Relay.Asked));
    }

    [Theory(DisplayName = "BR-BV-007: nothing is started where no application matches both identifiers and is to start by itself")]
    [InlineData(16, 2, HybridcastControlCode.Autostart)]
    [InlineData(17, 1, HybridcastControlCode.Autostart)]
    [InlineData(16, 1, HybridcastControlCode.Present)]
    public async Task NothingIsStartedWithoutAMatchingAutostart(int orgId, int appId, HybridcastControlCode controlCode)
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow();
        feature.Relay.Ait = new RelayedAit(RelayResult.Answered, 200, [HybridcastRelayFeature.Application(controlCode: controlCode)]);

        (HttpStatusCode status, _) = await feature.StartAsync(new { ait = HybridcastRelayFeature.AitAddress, orgId, appId, receiverScript = HybridcastRelayFeature.ReceiverScript });

        Assert.Equal(controlCode is HybridcastControlCode.Present || orgId != 16 || appId != 1 ? HttpStatusCode.NotFound : HttpStatusCode.OK, status);
    }

    [Fact(DisplayName = "BR-BD-013: nothing is started where no AIT could be read, and the answer is 502")]
    public async Task NothingIsStartedWithoutAnAit()
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow();
        feature.Relay.Ait = RelayedAit.Failed(RelayResult.NameNotResolved);

        (HttpStatusCode status, _) = await feature.StartAsync(new { ait = HybridcastRelayFeature.AitAddress, orgId = 16, appId = 1, receiverScript = HybridcastRelayFeature.ReceiverScript });

        Assert.Equal(HttpStatusCode.BadGateway, status);
    }

    [Fact(DisplayName = "BR-BD-011: nothing is started while Hybridcast is off")]
    public async Task NothingIsStartedWhileHybridcastIsOff()
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow(hybridcast: false);

        (HttpStatusCode status, JsonElement body) = await feature.StartAsync(new { ait = HybridcastRelayFeature.AitAddress, orgId = 16, appId = 1, receiverScript = HybridcastRelayFeature.ReceiverScript });

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Contains("Hybridcast", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(feature.Relay.Asked);
    }

    [Theory(DisplayName = "a start that names too little, or a receiver script anywhere but a plain path on this server, is refused")]
    [InlineData("""{"orgId":16,"appId":1,"receiverScript":"/r.js"}""")]
    [InlineData("""{"ait":"http://hc.station.example.jp/a.ait","orgId":0,"appId":1,"receiverScript":"/r.js"}""")]
    [InlineData("""{"ait":"http://hc.station.example.jp/a.ait","orgId":16,"appId":65536,"receiverScript":"/r.js"}""")]
    [InlineData("""{"ait":"http://hc.station.example.jp/a.ait","orgId":16,"appId":1}""")]
    [InlineData("""{"ait":"http://hc.station.example.jp/a.ait","orgId":16,"appId":1,"receiverScript":"//elsewhere.example/r.js"}""")]
    [InlineData("""{"ait":"http://hc.station.example.jp/a.ait","orgId":16,"appId":1,"receiverScript":"https://elsewhere.example/r.js"}""")]
    [InlineData("""{"ait":"http://hc.station.example.jp/a.ait","orgId":16,"appId":1,"receiverScript":"/r.js; script-src *"}""")]
    [InlineData("""{"ait":"http://hc.station.example.jp/a.ait","orgId":16,"appId":1,"receiverScript":"/a/../r.js"}""")]
    public async Task AStartThatNamesTooLittleIsRefused(string json)
    {
        await using HybridcastRelayFeature feature = new();
        feature.Allow();

        using StringContent content = new(json, System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await feature.Viewer.PostAsync(new Uri(HybridcastRelayFeature.Start, UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(feature.Relay.Asked);
    }

    [Fact(DisplayName = "a start without a session is refused")]
    public async Task AStartWithoutASessionIsRefused()
    {
        await using HybridcastRelayFeature feature = new();

        using HttpResponseMessage response = await feature.Frame.PostAsJsonAsync(
            new Uri(HybridcastRelayFeature.Start, UriKind.Relative),
            new { ait = HybridcastRelayFeature.AitAddress, orgId = 16, appId = 1, receiverScript = HybridcastRelayFeature.ReceiverScript });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
