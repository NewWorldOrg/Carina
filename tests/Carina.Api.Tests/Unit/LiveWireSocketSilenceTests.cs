using System.Net.WebSockets;
using System.Threading.Channels;

using Carina.Api.Live;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
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

        await WaitOutTheCeiling(clock);

        Assert.Equal(LiveDeparture.SourceWentQuiet, await carrying.WaitAsync(Eventually.Patience));
        Assert.Equal(WebSocketCloseStatus.InternalServerError, socket.Closed);
        Assert.Equal(LiveDepartures.Because(LiveDeparture.SourceWentQuiet), socket.ClosedBecause);
        Assert.Equal(Gateway.QuietsBeforeTheCeiling + 1, socket.Sent.Count);
        Assert.All(
            socket.Sent.Take(Gateway.QuietsBeforeTheCeiling),
            sent => Assert.Equal(
                [(byte)LiveControl.Ping],
                LiveFrame.Read(sent).Frame!.Payload.ToArray()));
    }

    [Fact]
    public async Task AWireTakingItsSupplyForGoneSaysSoOnTheControlChannelBeforeItIsClosed()
    {
        ScriptedWebSocket socket = new();
        HandTurnedClock clock = new();
        LiveStartupRecord startup = new(clock);
        LiveEndingRecord ending = new();
        LiveFanout fanout = new(new LiveFanoutSettings(), startup, ending);

        Started(startup);

        await using ILiveViewing viewing = await Joined(fanout);

        Task<LiveDeparture> carrying = Carrying(socket, viewing, clock);

        await WaitOutTheCeiling(clock);

        Assert.Equal(LiveDeparture.SourceWentQuiet, await carrying.WaitAsync(Eventually.Patience));
        Assert.Null(ending.Current);

        LiveFrame said = LiveFrame.Read(socket.Sent[^1]).Frame!;

        Assert.Equal(LiveChannel.Control, said.Channel);

        LiveEndingReading read = LiveEndingReport.Read(said.Payload.Span);

        Assert.Null(read.Fault);
        Assert.Equal(LiveSupplyEnd.WentQuiet, read.Report!.Why);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, socket.Closed);
        Assert.Equal(LiveDepartures.Because(LiveDeparture.SourceWentQuiet), socket.ClosedBecause);
    }

    [Fact]
    public async Task WhatTheSupplyItselfSaidIsPreferredToTheWireTakingItForGone()
    {
        ScriptedWebSocket socket = new();
        HandTurnedClock clock = new();
        LiveStartupRecord startup = new(clock);
        LiveEndingRecord ending = new();
        LiveFanout fanout = new(new LiveFanoutSettings(), startup, ending);

        Started(startup);
        ending.Note(LiveSupplyEnding.Of(LiveSupplyEnd.TakenForARecording, "a recording outranked it."));

        await using ILiveViewing viewing = await Joined(fanout);

        Task<LiveDeparture> carrying = Carrying(socket, viewing, clock);

        await WaitOutTheCeiling(clock);

        Assert.Equal(LiveDeparture.SourceWentQuiet, await carrying.WaitAsync(Eventually.Patience));

        LiveFrame said = LiveFrame.Read(socket.Sent[^1]).Frame!;

        Assert.Equal(LiveSupplyEnd.TakenForARecording, LiveEndingReport.Read(said.Payload.Span).Report!.Why);
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
        Assert.Equal(sent + 1, socket.Sent.Count);
        Assert.Equal(
            (byte)LiveChannel.Control,
            socket.Sent[^1][0]);
    }

    private static void Started(LiveStartupRecord startup)
    {
        foreach (LiveStartupSegment segment in LiveStartupSegments.InOrder)
        {
            startup.Reach(segment);
        }
    }

    private static async Task<ILiveViewing> Joined(LiveFanout fanout)
    {
        ILiveViewing? viewing = await fanout.JoinAsync(CancellationToken.None);

        Assert.NotNull(viewing);

        return viewing;
    }

    private static Task<LiveDeparture> Carrying(
        ScriptedWebSocket socket,
        Channel<LiveFrame> frames,
        TimeProvider clock)
        => new LiveWireSocket(socket, Gateway, null, null, clock).CarryAsync(
            frames.Reader,
            CancellationToken.None,
            CancellationToken.None);

    private static Task<LiveDeparture> Carrying(
        ScriptedWebSocket socket,
        ILiveViewing viewing,
        TimeProvider clock)
        => new LiveWireSocket(socket, Gateway, viewing.Startup, viewing.Ending, clock).CarryAsync(
            viewing.Frames,
            CancellationToken.None,
            CancellationToken.None);

    private static async Task WaitOutTheCeiling(HandTurnedClock clock)
    {
        for (int quiet = 0; quiet <= Gateway.QuietsBeforeTheCeiling; quiet++)
        {
            await WaitingOutTheQuiet(clock);
            clock.Turn(Gateway.BetweenPings);
        }
    }

    private static Task WaitingOutTheQuiet(HandTurnedClock clock)
        => Eventually.Happens(() => clock.Pending is 1, "the wire is waiting out the quiet");
}
