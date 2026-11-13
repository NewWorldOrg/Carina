using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading.Channels;

using Carina.Api.Live;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Api.Tests.Unit.App.Streaming;

[Trait("Category", "Unit")]
public sealed class LiveWireSocketTests
{
    private static readonly LiveWireSettings Impatient = new(
        betweenPings: TimeSpan.FromMilliseconds(30),
        writePatience: TimeSpan.FromMilliseconds(200));

    private static readonly byte[] Picture = [0x01, 0x02, 0x03];

    private static readonly LiveWireSettings Patient = new(
        betweenPings: TimeSpan.FromSeconds(10),
        writePatience: TimeSpan.FromMilliseconds(200));

    private static readonly LiveStartupSegment[] AsTheyHappen =
    [
        LiveStartupSegment.TunerSecured,
        LiveStartupSegment.TranscoderStarted,
        LiveStartupSegment.ChannelLocked,
        LiveStartupSegment.InitReached,
        LiveStartupSegment.FirstPicture,
    ];

    private static readonly LiveStartup Starting = LiveStartup.NotStarted
        .Reaching(LiveStartupSegment.TranscoderStarted, TimeSpan.FromSeconds(8))
        .Reaching(LiveStartupSegment.InitReached, TimeSpan.FromMilliseconds(8100));

    private static readonly LiveWireSettings Gateway = new(
        betweenPings: TimeSpan.FromSeconds(15),
        silenceCeiling: TimeSpan.FromSeconds(100));

    private static readonly LiveWireSettings ImpatientToWrite = new(
        betweenPings: TimeSpan.FromSeconds(30),
        writePatience: TimeSpan.FromMilliseconds(200));

    public static TheoryData<LiveSupplyEnd> EveryReasonASupplyEnds()
    {
        TheoryData<LiveSupplyEnd> reasons = [];

        foreach (LiveSupplyEnd why in Enum.GetValues<LiveSupplyEnd>())
        {
            reasons.Add(why);
        }

        return reasons;
    }

    [Fact]
    public async Task AFrameIsSentAsOneBinaryMessageCarryingItsOwnHeader()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.TryWrite(new LiveFrame(LiveChannel.Picture, LivePts.Of(90_000UL), Picture));
        frames.Writer.Complete();

        LiveDeparture departure = await Carry(socket, frames);

        Assert.Equal(LiveDeparture.SourceEnded, departure);
        Assert.Equal(
            [0x01, 0, 0, 0, 0, 0, 0x01, 0x5f, 0x90, 0x01, 0x02, 0x03],
            socket.Sent[0]);
    }

    [Fact]
    public async Task TheHeaderGoesOutBeforeTheFirstPictureThatNeedsIt()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.TryWrite(new LiveFrame(LiveChannel.PictureHeader, LivePts.Start, Picture));
        frames.Writer.TryWrite(new LiveFrame(LiveChannel.Picture, LivePts.Of(1UL), Picture));
        frames.Writer.Complete();

        await Carry(socket, frames);

        Assert.Equal(
            [(byte)LiveChannel.PictureHeader, (byte)LiveChannel.Picture, (byte)LiveChannel.Control],
            socket.Sent.Select(message => message[0]).ToArray());
    }

    [Fact]
    public async Task TheSourceRunningOutIsANormalEnding()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.Complete();

        Assert.Equal(LiveDeparture.SourceEnded, await Carry(socket, frames));
        Assert.True(socket.CloseAttempted);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.Closed);
    }

    [Fact]
    public async Task TheSourceBreakingIsNotANormalEnding()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.Complete(new InvalidOperationException("the transcoder stopped"));

        Assert.Equal(LiveDeparture.SourceBroke, await Carry(socket, frames));
        Assert.Equal(WebSocketCloseStatus.InternalServerError, socket.Closed);
    }

    [Fact]
    public void EveryEndingSaysWhyInWordsTheViewerIsGiven()
    {
        Assert.All(
            Enum.GetValues<LiveDeparture>(),
            departure => Assert.NotEmpty(LiveDepartures.Because(departure)));
    }

    [Fact]
    public void EveryWayTheSupplyItselfEndsNamesAReasonEvenWhenTheSupplySaidNothing()
    {
        Assert.Equal(LiveSupplyEnd.WentQuiet, LiveDepartures.Ending(LiveDeparture.SourceWentQuiet));
        Assert.Equal(LiveSupplyEnd.DriverLost, LiveDepartures.Ending(LiveDeparture.SourceEnded));
        Assert.Equal(LiveSupplyEnd.DriverLost, LiveDepartures.Ending(LiveDeparture.SourceBroke));
    }

    [Fact]
    public void TheFourWaysThatAreTheViewerOrTheAppRatherThanTheSupplyNameNoReason()
    {
        Assert.All(
            new[]
            {
                LiveDeparture.ViewerLeft,
                LiveDeparture.ViewerStoppedReading,
                LiveDeparture.SaidSomethingUnknown,
                LiveDeparture.SaidMoreThanTheWireTakes,
                LiveDeparture.ServerStopping,
            },
            departure => Assert.Null(LiveDepartures.Ending(departure)));
    }

    [Fact]
    public async Task AWireWhoseSupplyStoppedWithoutSayingWhyStillSaysThatMuch()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.Complete();

        Assert.Equal(LiveDeparture.SourceEnded, await Carry(socket, frames));

        LiveFrame said = LiveFrame.Read(Assert.Single(socket.Sent)).Frame!;

        Assert.Equal(LiveChannel.Control, said.Channel);
        Assert.Equal(LiveSupplyEnd.DriverLost, LiveEndingReport.Read(said.Payload.Span).Report!.Why);
    }

    [Fact]
    public async Task APingGoesOutWhileTheSourceHasNothingToSay()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();

        Task<LiveDeparture> carrying = Carry(socket, frames, stop.Token);

        await Until(() => socket.Sent.Count >= 2);
        await stop.CancelAsync();
        await carrying;

        Assert.All(
            socket.Sent,
            message => Assert.Equal(LiveControls.Frame(LiveControl.Ping).ToArray(), message));
    }

    [Fact]
    public async Task NoPingGoesOutAfterTheWireHasBeenToldToStop()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();

        await stop.CancelAsync();

        Assert.Equal(LiveDeparture.ViewerLeft, await Carry(socket, frames, stop.Token));
        Assert.Empty(socket.Sent);
    }

    [Fact]
    public async Task NoPingInterruptsAWireThatIsCarryingPictures()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        for (int written = 0; written < 20; written++)
        {
            frames.Writer.TryWrite(new LiveFrame(LiveChannel.Picture, LivePts.Of((ulong)written), Picture));
        }

        frames.Writer.Complete();

        await Carry(socket, frames);

        Assert.Equal(
            Enumerable.Repeat((byte)LiveChannel.Picture, 20).Append((byte)LiveChannel.Control),
            socket.Sent.Select(message => message[0]));
    }

    [Fact]
    public async Task AViewerSayingItIsLeavingIsAnsweredByLettingItGo()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(
            WebSocketMessageType.Binary,
            LiveControls.Frame(LiveControl.Leaving).ToArray()));

        Assert.Equal(LiveDeparture.ViewerLeft, await Carry(socket, frames));
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.Closed);
    }

    [Fact]
    public async Task AViewerClosingTheSocketIsTheSameAsSayingItIsLeaving()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(WebSocketMessageType.Close, []));

        Assert.Equal(LiveDeparture.ViewerLeft, await Carry(socket, frames));
    }

    [Fact]
    public async Task AViewerAnsweringAPingIsNotAnEnding()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(
            WebSocketMessageType.Binary,
            LiveControls.Frame(LiveControl.Pong).ToArray()));
        socket.Say(new WebSocketSaying(
            WebSocketMessageType.Binary,
            LiveControls.Frame(LiveControl.Leaving).ToArray()));

        Assert.Equal(LiveDeparture.ViewerLeft, await Carry(socket, frames));
    }

    [Fact]
    public async Task AViewerSayingSomethingThatIsNotAControlMessageIsShownOut()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(WebSocketMessageType.Binary, [0x40, 0, 0, 0, 0, 0, 0, 0, 0, 0x7f]));

        Assert.Equal(LiveDeparture.SaidSomethingUnknown, await Carry(socket, frames));
        Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, socket.Closed);
    }

    [Fact]
    public async Task AViewerRepeatingWhatOnlyTheServerSaysIsShownOut()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(
            WebSocketMessageType.Binary,
            LiveControls.Frame(LiveControl.Ping).ToArray()));

        Assert.Equal(LiveDeparture.SaidSomethingUnknown, await Carry(socket, frames));
    }

    [Fact]
    public async Task AViewerPushingBytesUpAPictureChannelIsShownOut()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(
            WebSocketMessageType.Binary,
            new LiveFrame(LiveChannel.Picture, LivePts.Start, Picture).ToArray()));

        Assert.Equal(LiveDeparture.SaidSomethingUnknown, await Carry(socket, frames));
    }

    [Fact]
    public async Task AViewerTypingWordsAtTheWireIsShownOut()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(WebSocketMessageType.Text, "seek 42"u8.ToArray()));

        Assert.Equal(LiveDeparture.SaidSomethingUnknown, await Carry(socket, frames));
    }

    [Fact]
    public async Task AViewerSendingMoreThanTheWireTakesIsShownOut()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        socket.Say(new WebSocketSaying(
            WebSocketMessageType.Binary,
            new byte[Impatient.LargestFrameFromAViewer + 64]));

        Assert.Equal(LiveDeparture.SaidMoreThanTheWireTakes, await Carry(socket, frames));
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, socket.Closed);
    }

    [Fact]
    public async Task AViewerThatStopsReadingIsGivenUpOnRatherThanHoldingTheSource()
    {
        var socket = new ScriptedWebSocket { HoldEverySend = TimeSpan.FromSeconds(30) };
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.TryWrite(new LiveFrame(LiveChannel.Picture, LivePts.Start, Picture));

        Assert.Equal(LiveDeparture.ViewerStoppedReading, await Carry(socket, frames));
        Assert.True(socket.Aborted);
        Assert.False(socket.CloseAttempted);
        Assert.Null(socket.Closed);
    }

    [Fact]
    public async Task AWireCarryingAFrameLargerThanAnythingTheFragmenterHoldsStillSendsItWhole()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.TryWrite(new LiveFrame(LiveChannel.Picture, LivePts.Start, new byte[4 * 1024 * 1024]));
        frames.Writer.Complete();

        await Carry(socket, frames);

        Assert.Equal(LiveFrame.HeaderLength + (4 * 1024 * 1024), socket.Sent[0].Length);
    }

    [Fact]
    public async Task TheAppShuttingDownIsNotTheViewerLeaving()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stopping = new CancellationTokenSource();
        using var stop = new CancellationTokenSource();

        Task<LiveDeparture> carrying = new LiveWireSocket(socket, Impatient)
            .CarryAsync(frames.Reader, stopping.Token, stop.Token);

        await stopping.CancelAsync();
        await stop.CancelAsync();

        Assert.Equal(LiveDeparture.ServerStopping, await carrying);
        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, socket.Closed);
    }

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

    [Theory]
    [MemberData(nameof(EveryReasonASupplyEnds))]
    public async Task AWireIsNeverClosedWithoutTheReasonHavingBeenSaidFirst(LiveSupplyEnd why)
    {
        ScriptedWebSocket socket = new();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        frames.Writer.Complete();

        LiveDeparture departure = await Carry(
            socket,
            frames,
            new HeldEnding(LiveSupplyEnding.Of(why, "the supply named a reason.")));

        Assert.Equal(LiveDeparture.SourceEnded, departure);

        LiveFrame said = LiveFrame.Read(Assert.Single(socket.Sent)).Frame!;

        Assert.Equal(LiveChannel.Control, said.Channel);
        Assert.Equal(why, LiveEndingReport.Read(said.Payload.Span).Report!.Why);
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

    [Fact]
    public async Task ProgressGoesOutRightAfterTheHandshakeWhileTheStartupIsStillRunning()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();

        Task<LiveDeparture> carrying = Carry(socket, frames, new HeldStartup(Starting), stop.Token);

        await UntilSaid(() => socket.Sent.Count >= 1);
        await stop.CancelAsync();
        await carrying;

        LiveFrame first = LiveFrame.Read(socket.Sent[0]).Frame!;

        Assert.Equal(LiveChannel.Control, first.Channel);

        LiveStartupReading read = LiveStartup.ReadProgress(first.Payload.Span);

        Assert.Null(read.Fault);
        Assert.Equal(TimeSpan.FromSeconds(8), read.Startup!.At(LiveStartupSegment.TranscoderStarted));
        Assert.Equal(TimeSpan.FromMilliseconds(8100), read.Startup.At(LiveStartupSegment.InitReached));
        Assert.False(read.Startup.Reached(LiveStartupSegment.FirstPicture));
    }

    [Fact]
    public async Task AProgressReportRidesTheControlChannelButIsNotSomethingAViewerCouldEverSay()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();

        Task<LiveDeparture> carrying = Carry(socket, frames, new HeldStartup(Starting), stop.Token);

        await UntilSaid(() => socket.Sent.Count >= 1);
        await stop.CancelAsync();
        await carrying;

        LiveFrame first = LiveFrame.Read(socket.Sent[0]).Frame!;

        Assert.Equal(LiveChannel.Control, first.Channel);
        Assert.NotEqual(1, first.Payload.Length);
        Assert.Null(LiveControls.SaidByAViewer(first.Payload.Span));
    }

    [Fact]
    public async Task OnceTheFirstPictureIsReachedTheWireFallsBackToTheKeepAlivePing()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();

        LiveStartup done = Starting.Reaching(LiveStartupSegment.FirstPicture, TimeSpan.FromMilliseconds(10100));

        Task<LiveDeparture> carrying = Carry(socket, frames, new HeldStartup(done), stop.Token);

        await UntilSaid(() => socket.Sent.Count >= 2);
        await stop.CancelAsync();
        await carrying;

        Assert.All(
            socket.Sent,
            message => Assert.Equal(LiveControls.Frame(LiveControl.Ping).ToArray(), message));
    }

    [Fact]
    public async Task AWireWithNothingToReportSaysNothingButTheKeepAlivePing()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();

        Task<LiveDeparture> carrying = Carry(socket, frames, new HeldStartup(null), stop.Token);

        await UntilSaid(() => socket.Sent.Count >= 2);
        await stop.CancelAsync();
        await carrying;

        Assert.All(
            socket.Sent,
            message => Assert.Equal(LiveControls.Frame(LiveControl.Ping).ToArray(), message));
    }

    [Fact]
    public async Task AReportGoesOutEachTimeASegmentIsReachedNotOnlyWhenTheWireFallsQuiet()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();
        var startup = new SteppingStartup();

        Task<LiveDeparture> carrying = new LiveWireSocket(socket, Patient, startup)
            .CarryAsync(frames.Reader, CancellationToken.None, stop.Token);

        await UntilSaid(() => socket.Sent.Count >= 1);

        int reached = 0;

        foreach (LiveStartupSegment segment in AsTheyHappen)
        {
            reached++;
            startup.Reach(segment, TimeSpan.FromMilliseconds(100 * reached));

            int expected = reached + 1;

            await UntilSaid(() => socket.Sent.Count >= expected);
        }

        await Task.Delay(150);
        await stop.CancelAsync();
        await carrying;

        LiveStartup[] reports = [.. socket.Sent.Select(Reported)];

        Assert.Equal(6, reports.Length);
        Assert.Equal([0, 1, 2, 3, 4, 5], reports.Select(report => report.Timeline.Count(mark => mark.Reached)));
        Assert.True(reports[2].Reached(LiveStartupSegment.TranscoderStarted));
        Assert.False(reports[2].Reached(LiveStartupSegment.ChannelLocked));
        Assert.False(reports[^1].InProgress);
    }

    [Fact]
    public async Task AStepTheWireHadAlreadyReportedIsNotReportedAgainUntilTheNextOne()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();
        var startup = new SteppingStartup();

        startup.Reach(LiveStartupSegment.TunerSecured, TimeSpan.FromMilliseconds(100));
        startup.Reach(LiveStartupSegment.TranscoderStarted, TimeSpan.FromMilliseconds(110));

        Task<LiveDeparture> carrying = new LiveWireSocket(socket, Patient, startup)
            .CarryAsync(frames.Reader, CancellationToken.None, stop.Token);

        await UntilSaid(() => socket.Sent.Count >= 1);
        await Task.Delay(150);

        Assert.Single(socket.Sent);

        startup.Reach(LiveStartupSegment.ChannelLocked, TimeSpan.FromMilliseconds(700));

        await UntilSaid(() => socket.Sent.Count >= 2);
        await Task.Delay(150);
        await stop.CancelAsync();
        await carrying;

        Assert.Equal(2, socket.Sent.Count);
        Assert.Equal(2, Reported(socket.Sent[0]).Timeline.Count(mark => mark.Reached));
        Assert.Equal(3, Reported(socket.Sent[1]).Timeline.Count(mark => mark.Reached));
    }

    [Fact]
    public async Task TheKeepAlivePingTakesOverOnceTheLastStepHasBeenReported()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();
        using var stop = new CancellationTokenSource();
        var startup = new SteppingStartup();

        Task<LiveDeparture> carrying = new LiveWireSocket(socket, Impatient, startup)
            .CarryAsync(frames.Reader, CancellationToken.None, stop.Token);

        await UntilSaid(() => socket.Sent.Count >= 1);

        foreach (LiveStartupSegment segment in AsTheyHappen)
        {
            startup.Reach(segment, TimeSpan.FromMilliseconds(100));
        }

        await UntilSaid(() => socket.Sent.Any(message => message.SequenceEqual(LiveControls.Frame(LiveControl.Ping).ToArray())));
        await stop.CancelAsync();
        await carrying;

        byte[][] afterTheLast = [.. socket.Sent.SkipWhile(message => !Reported(message, out LiveStartup? report) || report!.InProgress).Skip(1)];

        Assert.NotEmpty(afterTheLast);
        Assert.All(afterTheLast, message => Assert.Equal(LiveControls.Frame(LiveControl.Ping).ToArray(), message));
    }

    [Fact]
    public async Task ProgressDoesNotInterruptTheFramesTheSourceIsAlreadySending()
    {
        var socket = new ScriptedWebSocket();
        Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        for (int written = 0; written < 10; written++)
        {
            frames.Writer.TryWrite(new LiveFrame(LiveChannel.Picture, LivePts.Of((ulong)written), Picture));
        }

        frames.Writer.Complete();

        await Carry(socket, frames, new HeldStartup(Starting), CancellationToken.None);

        Assert.Equal(10, socket.Sent.Count(message => message[0] == (byte)LiveChannel.Picture));
    }

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

    [Theory]
    [InlineData(TuneFailureKind.NoLock)]
    [InlineData(TuneFailureKind.NoData)]
    [InlineData(TuneFailureKind.IncompletePsi)]
    [InlineData(TuneFailureKind.StreamMismatch)]
    public async Task BrTd004TheReasonATuningFailedLeavesTheWireBesideTheRefusalRatherThanOnlyInTheNote(
        TuneFailureKind kind)
    {
        ScriptedWebSocket socket = new();

        await new LiveWireSocket(socket, new LiveWireSettings()).RefuseAsync(
            LiveJoin.Refused(LiveRefusal.WouldNotTune, "no lock.", LiveRefusalDetail.Of(kind)),
            CancellationToken.None);

        LiveRefusalReading read = LiveRefusalReport.Read(LiveFrame.Read(socket.Sent[0]).Frame!.Payload.Span);

        Assert.Null(read.Fault);
        Assert.Equal(LiveRefusal.WouldNotTune, read.Report!.Refusal);
        Assert.Equal(kind, read.Report.Detail.TuneFailure);
    }

    [Fact]
    public async Task Fr012WhatHoldsTheTunerLeavesTheWireBesideTheRefusalTheSameWay()
    {
        ScriptedWebSocket socket = new();

        await new LiveWireSocket(socket, new LiveWireSettings()).RefuseAsync(
            LiveJoin.Refused(
                LiveRefusal.NoTunerFree,
                "a recording holds it.",
                LiveRefusalDetail.Of(LiveTunerHolder.ARecording)),
            CancellationToken.None);

        LiveRefusalReading read = LiveRefusalReport.Read(LiveFrame.Read(socket.Sent[0]).Frame!.Payload.Span);

        Assert.Null(read.Fault);
        Assert.Equal(LiveRefusal.NoTunerFree, read.Report!.Refusal);
        Assert.Equal(LiveTunerHolder.ARecording, read.Report.Detail.Holder);
        Assert.Equal(LiveRefusalReport.PayloadLength, LiveFrame.Read(socket.Sent[0]).Frame!.Payload.Length);
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

    [Fact]
    public async Task OneViewerHoldingItsSocketNeitherHoldsUpAnotherNorTheSource()
    {
        LiveFanout fanout = new(new LiveFanoutSettings { LongestBacklog = 3 });
        ScriptedWebSocket prompt = new();
        ScriptedWebSocket held = new() { HoldEverySend = TimeSpan.FromSeconds(30) };
        await using ILiveViewing promptViewing = await Joined(fanout);
        await using ILiveViewing heldViewing = await Joined(fanout);

        Task<LiveDeparture> promptCarrying = Carry(prompt, promptViewing);
        Task<LiveDeparture> heldCarrying = Carry(held, heldViewing);

        for (ulong pts = 0; pts < 20; pts++)
        {
            fanout.Publish(new LiveFrame(LiveChannel.Picture, LivePts.Of(pts), Picture));
            await Eventually.Happens(() => prompt.Sent.Count == (int)pts + 1, "the prompt socket takes the frame");
        }

        fanout.End();

        Assert.Equal(LiveDeparture.SourceEnded, await promptCarrying);
        Assert.Equal(LiveDeparture.ViewerStoppedReading, await heldCarrying);
        Assert.Equal(
            Enumerable.Repeat((byte)LiveChannel.Picture, 20).Append((byte)LiveChannel.Control),
            prompt.Sent.Select(message => message[0]));
        Assert.Equal(LiveBacklog.Empty, promptViewing.Backlog);
        Assert.InRange(heldViewing.Backlog.Dropped, 16L, 17L);
        Assert.Empty(held.Sent);
        Assert.True(held.Aborted);
    }

    [Fact]
    public async Task TheHeaderReachesAViewerThatJoinedAfterItWasSentBeforeAnyPicture()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());
        ScriptedWebSocket socket = new();

        fanout.Publish(new LiveFrame(LiveChannel.PictureHeader, LivePts.Start, Picture));
        fanout.Publish(new LiveFrame(LiveChannel.Picture, LivePts.Of(1UL), Picture));

        await using ILiveViewing viewing = await Joined(fanout);

        Task<LiveDeparture> carrying = Carry(socket, viewing);

        fanout.Publish(new LiveFrame(LiveChannel.Picture, LivePts.Of(2UL), Picture));
        fanout.End();

        Assert.Equal(LiveDeparture.SourceEnded, await carrying);
        Assert.Equal(
            [(byte)LiveChannel.PictureHeader, (byte)LiveChannel.Picture, (byte)LiveChannel.Control],
            socket.Sent.Select(message => message[0]).ToArray());
        Assert.Equal(2UL, LiveFrame.Read(socket.Sent[1]).Frame?.Pts.Value);
    }

    [Fact]
    public async Task TheSourceBreakingReachesEveryWireAsABreakRatherThanAnEnd()
    {
        LiveFanout fanout = new(new LiveFanoutSettings());
        ScriptedWebSocket one = new();
        ScriptedWebSocket another = new();
        await using ILiveViewing oneViewing = await Joined(fanout);
        await using ILiveViewing anotherViewing = await Joined(fanout);

        Task<LiveDeparture> oneCarrying = Carry(one, oneViewing);
        Task<LiveDeparture> anotherCarrying = Carry(another, anotherViewing);

        fanout.Break(LiveFragmentFault.StoppedPartWayThrough);

        Assert.Equal(LiveDeparture.SourceBroke, await oneCarrying);
        Assert.Equal(LiveDeparture.SourceBroke, await anotherCarrying);
    }

    private static Task<LiveDeparture> Carry(
        ScriptedWebSocket socket,
        Channel<LiveFrame> frames,
        CancellationToken cancellationToken = default)
        => new LiveWireSocket(socket, Impatient)
            .CarryAsync(frames.Reader, CancellationToken.None, cancellationToken);

    private static async Task Until(Func<bool> settled)
    {
        for (int tries = 0; tries < 200; tries++)
        {
            if (settled())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail("The wire never carried what was expected of it.");
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

    private static Task<LiveDeparture> Carry(
        ScriptedWebSocket socket,
        Channel<LiveFrame> frames,
        ILiveStartup startup,
        CancellationToken cancellationToken)
        => new LiveWireSocket(socket, Impatient, startup)
            .CarryAsync(frames.Reader, CancellationToken.None, cancellationToken);

    private static async Task UntilSaid(Func<bool> settled)
    {
        for (int tries = 0; tries < 200; tries++)
        {
            if (settled())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail("The wire never said what was expected of it.");
    }

    private static LiveStartup Reported(byte[] message)
    {
        Assert.True(Reported(message, out LiveStartup? report), "the wire said something that is not a progress report.");

        return report!;
    }

    private static bool Reported(byte[] message, out LiveStartup? report)
    {
        report = null;

        if (LiveFrame.Read(message).Frame is not { Channel: LiveChannel.Control } frame
            || frame.Payload.Length != LiveStartup.PayloadLength)
        {
            return false;
        }

        report = LiveStartup.ReadProgress(frame.Payload.Span).Startup;

        return report is not null;
    }

    private sealed class HeldStartup(LiveStartup? current) : ILiveStartup
    {
        public LiveStartup? Current { get; } = current;

        public Task Advanced { get; } = new TaskCompletionSource().Task;
    }

    private sealed class SteppingStartup : ILiveStartup
    {
        private readonly Lock gate = new();

        private TaskCompletionSource advanced = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private LiveStartup current = LiveStartup.NotStarted;

        public LiveStartup? Current
        {
            get
            {
                lock (gate)
                {
                    return current;
                }
            }
        }

        public Task Advanced
        {
            get
            {
                lock (gate)
                {
                    return advanced.Task;
                }
            }
        }

        public void Reach(LiveStartupSegment segment, TimeSpan at)
        {
            TaskCompletionSource told;

            lock (gate)
            {
                current = current.Reaching(segment, at);
                told = advanced;
                advanced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            told.SetResult();
        }
    }

    private sealed class SeatedNowhere : ILiveViewing
    {
        public ChannelReader<LiveFrame> Frames { get; } = Channel.CreateUnbounded<LiveFrame>().Reader;

        public LiveBacklog Backlog => LiveBacklog.Empty;

        public ILiveStartup? Startup => null;

        public ILiveEnding? Ending => null;

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
        => Eventually.Happens(
            () => clock.Waiting.SequenceEqual([Gateway.BetweenPings]),
            "the wire is waiting out the quiet");

    private static Task<LiveDeparture> Carry(ScriptedWebSocket socket, ILiveViewing viewing)
        => new LiveWireSocket(socket, ImpatientToWrite)
            .CarryAsync(viewing.Frames, CancellationToken.None, CancellationToken.None);
}
