using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading.Channels;

using Carina.Api.Live;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.Unit;

public sealed class LiveWireSocketRefusalTests
{
    [Fact]
    public async Task ARefusalIsSaidOnTheControlChannelAndThenTheWireIsClosedWithItsReason()
    {
        ScriptedWebSocket socket = new();

        await new LiveWireSocket(socket, new LiveWireSettings()).RefuseAsync(
            LiveJoin.Refused(LiveRefusal.NoTunerFree, "every tuner is recording."),
            CancellationToken.None);

        LiveFrame said = LiveFrame.Read(Assert.Single(socket.Sent)).Frame!;

        Assert.Equal(LiveChannel.Control, said.Channel);
        Assert.Equal(LivePts.Start, said.Pts);

        LiveRefusalReading read = LiveRefusalReport.Read(said.Payload.Span);

        Assert.Null(read.Fault);
        Assert.Equal(LiveRefusal.NoTunerFree, read.Report!.Refusal);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, socket.Closed);
        Assert.Equal(LiveRefusalClosures.Because(LiveRefusal.NoTunerFree), socket.ClosedBecause);
        Assert.False(socket.Aborted);
    }

    [Fact]
    public async Task AFullBudgetIsSaidWithItsCeiling()
    {
        ScriptedWebSocket socket = new();

        await new LiveWireSocket(socket, new LiveWireSettings()).RefuseAsync(
            LiveJoin.Refused(new TranscodeCeiling(4, 4)),
            CancellationToken.None);

        LiveRefusalReading read = LiveRefusalReport.Read(LiveFrame.Read(socket.Sent[0]).Frame!.Payload.Span);

        Assert.Equal(LiveRefusal.TooManyAlready, read.Report!.Refusal);
        Assert.Equal(new TranscodeCeiling(4, 4), read.Report.Ceiling);
    }

    [Fact]
    public async Task WhatIsSaidIsNeitherAPingNorAProgressReportNorSomethingAViewerCouldSay()
    {
        ScriptedWebSocket socket = new();

        await new LiveWireSocket(socket, new LiveWireSettings()).RefuseAsync(
            LiveJoin.Refused(LiveRefusal.DriverUnavailable, "nothing supplies a stream."),
            CancellationToken.None);

        LiveFrame said = LiveFrame.Read(socket.Sent[0]).Frame!;

        Assert.Equal(LiveRefusalReport.PayloadLength, said.Payload.Length);
        Assert.Null(LiveControls.SaidByAViewer(said.Payload.Span));
        Assert.NotNull(LiveStartup.ReadProgress(said.Payload.Span).Fault);
    }

    [Fact]
    public async Task ASeatedViewerCannotBeRefused()
    {
        ScriptedWebSocket socket = new();

        await Assert.ThrowsAsync<ArgumentException>(() => new LiveWireSocket(socket, new LiveWireSettings()).RefuseAsync(
            LiveJoin.Joined(new SeatedNowhere()),
            CancellationToken.None));

        Assert.Empty(socket.Sent);
    }

    [Fact]
    public async Task AViewerThatNeverAnswersTheCloseIsCutOffRatherThanWaitedFor()
    {
        SilentWebSocket socket = new();

        await new LiveWireSocket(socket, new LiveWireSettings()).RefuseAsync(
            LiveJoin.Refused(LiveRefusal.WouldNotTune, "no lock."),
            CancellationToken.None);

        Assert.True(socket.Aborted);
    }

    private sealed class SeatedNowhere : ILiveViewing
    {
        public ChannelReader<LiveFrame> Frames { get; } = Channel.CreateUnbounded<LiveFrame>().Reader;

        public LiveBacklog Backlog => LiveBacklog.Empty;

        public ILiveStartup? Startup => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SilentWebSocket : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override string? SubProtocol => null;

        public override WebSocketState State => WebSocketState.Open;

        public bool Aborted { get; private set; }

        public override void Abort() => Aborted = true;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override void Dispose()
        {
        }

        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            throw new UnreachableException();
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
