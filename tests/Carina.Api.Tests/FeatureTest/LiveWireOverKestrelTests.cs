using System.Net.WebSockets;

using Carina.Api.Live;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.FeatureTest;

public sealed class LiveWireOverKestrelTests
{
    private static readonly byte[] Picture = [0x0a, 0x0b, 0x0c];

    [Fact]
    public async Task AViewerThatStopsReadingOverRealKestrelIsRecordedAsHavingStoppedReading()
    {
        var held = new HeldLiveSource();
        await using LiveKestrelHost host = await LiveKestrelHost.StartAsync(
            held,
            new LiveWireSettings
            {
                BetweenPings = TimeSpan.FromSeconds(30),
                WritePatience = TimeSpan.FromMilliseconds(300),
            });

        using var client = new ClientWebSocket();
        await client.ConnectAsync(host.Wire, Patiently());

        held.Send(new LiveFrame(LiveChannel.Picture, LivePts.Start, new byte[8 * 1024 * 1024]));

        LiveDeparture departure = await host.DepartureAsync(Patiently());

        Assert.Equal(LiveDeparture.ViewerStoppedReading, departure);
    }

    [Fact]
    public async Task TheSourceRunningOutIsReadByARealClientAsACleanCloseItCanNameTheReasonOf()
    {
        var held = new HeldLiveSource();
        await using LiveKestrelHost host = await LiveKestrelHost.StartAsync(held);

        using var client = new ClientWebSocket();
        await client.ConnectAsync(host.Wire, Patiently());

        held.Send(new LiveFrame(LiveChannel.Picture, LivePts.Of(90_000UL), Picture));
        held.NoMore();

        WebSocketReceiveResult ending = await ReadUntilClose(client);

        Assert.Equal(WebSocketMessageType.Close, ending.MessageType);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, client.CloseStatus);
        Assert.Equal(LiveDepartures.Because(LiveDeparture.SourceEnded), client.CloseStatusDescription);
        Assert.Equal(LiveDeparture.SourceEnded, await host.DepartureAsync(Patiently()));
    }

    private static async Task<WebSocketReceiveResult> ReadUntilClose(ClientWebSocket client)
    {
        byte[] heard = new byte[64 * 1024];

        while (true)
        {
            WebSocketReceiveResult said = await client.ReceiveAsync(new ArraySegment<byte>(heard), Patiently());

            if (said.MessageType is WebSocketMessageType.Close)
            {
                return said;
            }
        }
    }

    private static CancellationToken Patiently() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;
}
