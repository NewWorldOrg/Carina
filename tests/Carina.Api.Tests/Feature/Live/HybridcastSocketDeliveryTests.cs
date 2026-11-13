using System.Net;
using System.Net.WebSockets;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Feature.Live;

[Trait("Category", "Feature")]
public sealed class HybridcastSocketDeliveryTests
{
    [Fact(DisplayName = "FR-016: a WebSocket inside the boundary is carried both ways, and the application's close is answered")]
    public async Task AWebSocketIsCarriedBothWays()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        WebSocketClient client = feature.Served.Server.CreateWebSocketClient();
        client.SubProtocols.Add("hc.v1");

        using WebSocket socket = await client.ConnectAsync(HybridcastRelayFeature.Socket(started, "ws://hc.station.example.jp/live?id=1"), CancellationToken.None);
        await socket.SendAsync("hello"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
        byte[] buffer = new byte[64];
        WebSocketReceiveResult echoed = await socket.ReceiveAsync(buffer, CancellationToken.None);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        WebSocketReceiveResult closed = await socket.ReceiveAsync(buffer, CancellationToken.None);

        Assert.Equal("hc.v1", socket.SubProtocol);
        Assert.Equal("echo:hello", System.Text.Encoding.UTF8.GetString(buffer, 0, echoed.Count));
        Assert.Equal(WebSocketMessageType.Close, closed.MessageType);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, closed.CloseStatus);
        Assert.Equal("socket 32736-1024 ws://hc.station.example.jp/live?id=1 hc.v1", feature.Relay.Asked.Last());
    }

    [Fact(DisplayName = "BR-BA-004: a WebSocket outside the boundary is refused 403 and opened to nobody")]
    public async Task AWebSocketOutsideTheBoundaryIsRefused()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        WebSocketClient client = feature.Served.Server.CreateWebSocketClient();

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ConnectAsync(HybridcastRelayFeature.Socket(started, "wss://elsewhere.example/live"), CancellationToken.None));

        Assert.Contains("403", refused.Message, StringComparison.Ordinal);
        Assert.Single(feature.Relay.Asked);
    }

    [Fact(DisplayName = "the WebSocket relay asked for as a page is refused")]
    public async Task TheWebSocketRelayAskedForAsAPageIsRefused()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();

        using HttpResponseMessage response = await feature.Frame.GetAsync(new Uri(HybridcastRelayFeature.Socket(started, "ws://hc.station.example.jp/live").PathAndQuery, UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory(DisplayName = "BR-BA-004: the frame's WebSocket is opened from an origin of null, from this installation or from no origin at all")]
    [InlineData("null")]
    [InlineData("http://localhost")]
    [InlineData(null)]
    public async Task TheFramesWebSocketIsOpenedFromAnOriginOfNull(string? origin)
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        WebSocketClient client = feature.Served.Server.CreateWebSocketClient();

        if (origin is not null)
        {
            client.ConfigureRequest += request => request.Headers[HeaderNames.Origin] = origin;
        }

        using WebSocket socket = await client.ConnectAsync(HybridcastRelayFeature.Socket(started, "ws://hc.station.example.jp/live"), CancellationToken.None);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

        Assert.Equal("socket 32736-1024 ws://hc.station.example.jp/live ", feature.Relay.Asked.Last());
    }

    [Fact(DisplayName = "BR-BA-004: the frame's WebSocket asked for from another named origin is refused 403 and opened to nobody")]
    public async Task TheFramesWebSocketFromAnotherOriginIsRefused()
    {
        await using HybridcastRelayFeature feature = new();
        JsonElement started = await feature.StartedAsync();
        WebSocketClient client = feature.Served.Server.CreateWebSocketClient();
        client.ConfigureRequest += request => request.Headers[HeaderNames.Origin] = "https://elsewhere.example";
        int askedBefore = feature.Relay.Asked.Count;

        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ConnectAsync(HybridcastRelayFeature.Socket(started, "ws://hc.station.example.jp/live"), CancellationToken.None));

        Assert.Contains("403", refused.Message, StringComparison.Ordinal);
        Assert.Equal(askedBefore, feature.Relay.Asked.Count);
    }
}
