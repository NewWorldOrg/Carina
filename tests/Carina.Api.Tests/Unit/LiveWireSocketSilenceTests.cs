using System.Net.WebSockets;
using System.Threading.Channels;

using Carina.Api.Live;
using Carina.Domain.Streaming;
using Carina.TestSupport;

namespace Carina.Api.Tests.Unit;

public sealed class LiveWireSocketSilenceTests
{
    private static readonly LiveWireSettings Gateway = new()
    {
        BetweenPings = TimeSpan.FromSeconds(15),
        SilenceCeiling = TimeSpan.FromSeconds(100),
    };

    private static readonly byte[] Picture = [0x01, 0x02, 0x03];

    [Fact]
    public async Task ASupplyThatSaysNothingPastTheCeilingIsTakenForGoneAndTheWireIsClosed()
    {
        ScriptedWebSocket socket = new();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        HandTurnedClock clock = new();

        Task<LiveDeparture> carrying = Carrying(socket, frames, clock);

        for (int quiet = 0; quiet <= Gateway.QuietsBeforeTheCeiling; quiet++)
        {
            await WaitingOutTheQuiet(clock);
            clock.Turn(Gateway.BetweenPings);
        }

        Assert.Equal(LiveDeparture.SourceWentQuiet, await carrying.WaitAsync(Eventually.Patience));
        Assert.Equal(WebSocketCloseStatus.InternalServerError, socket.Closed);
        Assert.Equal(LiveDepartures.Because(LiveDeparture.SourceWentQuiet), socket.ClosedBecause);
        Assert.Equal(Gateway.QuietsBeforeTheCeiling, socket.Sent.Count);
        Assert.All(
            socket.Sent,
            sent => Assert.Equal(
                [(byte)LiveControl.Ping],
                LiveFrame.Read(sent).Frame!.Payload.ToArray()));
    }

    [Fact]
    public async Task TheCeilingIsCountedFromTheLastFrameSoAWireStillBeingFedIsNeverClosedForQuiet()
    {
        ScriptedWebSocket socket = new();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        HandTurnedClock clock = new();

        Task<LiveDeparture> carrying = Carrying(socket, frames, clock);

        int sent = 0;

        for (int rounds = 0; rounds < 4; rounds++)
        {
            for (int quiet = 0; quiet < Gateway.QuietsBeforeTheCeiling; quiet++)
            {
                await WaitingOutTheQuiet(clock);
                clock.Turn(Gateway.BetweenPings);
                sent++;
            }

            frames.Writer.TryWrite(new LiveFrame(LiveChannel.Picture, LivePts.Start, Picture));
            sent++;

            await Eventually.Happens(() => socket.Sent.Count == sent, "the picture reaches the wire");
        }

        Assert.False(carrying.IsCompleted);
        Assert.Null(socket.Closed);

        frames.Writer.Complete();

        Assert.Equal(LiveDeparture.SourceEnded, await carrying);
    }

    private static Task<LiveDeparture> Carrying(
        ScriptedWebSocket socket,
        Channel<LiveFrame> frames,
        TimeProvider clock)
        => new LiveWireSocket(socket, Gateway, null, null, clock).CarryAsync(
            frames.Reader,
            CancellationToken.None,
            CancellationToken.None);

    private static Task WaitingOutTheQuiet(HandTurnedClock clock)
        => Eventually.Happens(() => clock.Pending is 1, "the wire is waiting out the quiet");
}
