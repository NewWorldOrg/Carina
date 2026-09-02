using System.Net.WebSockets;

using Carina.Domain.Streaming;

namespace Carina.Api.Tests.FeatureTest;

public sealed class LiveWireOverKestrelTests
{
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

    private static CancellationToken Patiently() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;
}
