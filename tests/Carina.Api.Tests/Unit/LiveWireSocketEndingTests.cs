using System.Net.WebSockets;
using System.Threading.Channels;

using Carina.Api.Live;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.Unit;

public sealed class LiveWireSocketEndingTests
{
    private static readonly byte[] Picture = [0x01, 0x02, 0x03];

    [Fact]
    public async Task WhyTheSupplyEndedIsSaidOnTheControlChannelAfterTheLastFrameAndBeforeTheClose()
    {
        ScriptedWebSocket socket = new();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        HeldEnding ending = new(LiveSupplyEnding.Of(LiveSupplyEnd.TakenForARecording, "a recording outranked it."));

        frames.Writer.TryWrite(new LiveFrame(LiveChannel.Picture, LivePts.Of(90_000UL), Picture));
        frames.Writer.Complete();

        LiveDeparture departure = await Carry(socket, frames, ending);

        Assert.Equal(LiveDeparture.SourceEnded, departure);
        Assert.Equal(2, socket.Sent.Count);
        Assert.Equal(LiveChannel.Picture, LiveFrame.Read(socket.Sent[0]).Frame!.Channel);

        LiveFrame said = LiveFrame.Read(socket.Sent[1]).Frame!;

        Assert.Equal(LiveChannel.Control, said.Channel);

        LiveEndingReading read = LiveEndingReport.Read(said.Payload.Span);

        Assert.Null(read.Fault);
        Assert.Equal(LiveSupplyEnd.TakenForARecording, read.Report!.Why);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.Closed);
        Assert.Equal(LiveDepartures.Because(LiveDeparture.SourceEnded), socket.ClosedBecause);
    }

    [Fact]
    public async Task ASourceThatBreaksStillSaysWhyItEndedWhenTheSupplyKnows()
    {
        ScriptedWebSocket socket = new();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        HeldEnding ending = new(LiveSupplyEnding.Of(LiveSupplyEnd.DriverLost, "the driver went away."));

        frames.Writer.Complete(new InvalidOperationException("What was being sent live broke."));

        LiveDeparture departure = await Carry(socket, frames, ending);

        Assert.Equal(LiveDeparture.SourceBroke, departure);

        LiveFrame said = LiveFrame.Read(Assert.Single(socket.Sent)).Frame!;

        Assert.Equal(LiveSupplyEnd.DriverLost, LiveEndingReport.Read(said.Payload.Span).Report!.Why);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, socket.Closed);
    }

    [Fact]
    public async Task ASourceThatEndsWithoutTheSupplyHavingSaidWhySaysNothingButTheClose()
    {
        ScriptedWebSocket socket = new();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.Complete();

        LiveDeparture departure = await Carry(socket, frames, new HeldEnding(null));

        Assert.Equal(LiveDeparture.SourceEnded, departure);
        Assert.Empty(socket.Sent);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.Closed);
    }

    [Fact]
    public async Task WhatIsSaidIsNeitherAPingNorARefusalNorAProgressReportNorSomethingAViewerCouldSay()
    {
        ScriptedWebSocket socket = new();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.Complete();

        await Carry(socket, frames, new HeldEnding(LiveSupplyEnding.Of(LiveSupplyEnd.WindowClosed, "the window closed.")));

        LiveFrame said = LiveFrame.Read(Assert.Single(socket.Sent)).Frame!;

        Assert.Equal(LiveEndingReport.PayloadLength, said.Payload.Length);
        Assert.Null(LiveControls.SaidByAViewer(said.Payload.Span));
        Assert.NotNull(LiveRefusalReport.Read(said.Payload.Span).Fault);
        Assert.NotNull(LiveStartup.ReadProgress(said.Payload.Span).Fault);
    }

    private static Task<LiveDeparture> Carry(ScriptedWebSocket socket, Channel<LiveFrame> frames, ILiveEnding ending)
        => new LiveWireSocket(socket, new LiveWireSettings(), null, ending).CarryAsync(
            frames.Reader,
            CancellationToken.None,
            CancellationToken.None);

    private sealed class HeldEnding(LiveSupplyEnding? current) : ILiveEnding
    {
        public LiveSupplyEnding? Current => current;
    }
}
