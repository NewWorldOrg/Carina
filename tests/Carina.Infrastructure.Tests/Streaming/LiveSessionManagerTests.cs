using System.Reflection;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class LiveSessionManagerTests
{
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan LongestRaise = TimeSpan.FromSeconds(30);

    private static readonly LiveSessionKey EveryFrame = new(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd30);

    private static readonly LiveSessionKey EveryField = new(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd60);

    private static readonly LiveSessionKey AnotherChannel = new(new NetworkId(32736), new ServiceId(1032), LiveProfile.Hd30);

    private readonly HandTurnedClock clock = new();

    private readonly PipedSupply supply = new();

    private readonly TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 4 });

    private readonly HeldTranscoders transcoders;

    private readonly HeldCaptioners captioners = new();

    private readonly SilentEvents events = new();

    private readonly LiveSessionManager manager;

    private ILiveSessionLedger Ledger => manager;

    public LiveSessionManagerTests()
    {
        transcoders = new HeldTranscoders(budget);
        manager = Managing(budget, transcoders);
    }

    [Fact]
    public async Task ASecondViewerWithTheSameKeyRidesTheTranscoderTheFirstOneRaised()
    {
        await using ILiveViewing first = await Joined(EveryFrame);
        await using ILiveViewing second = await Joined(EveryFrame);

        Assert.Equal(1, transcoders.Started);
        Assert.Equal(1, budget.Running);
        Assert.Equal(1, supply.Asked);
        Assert.Equal(2, manager.Viewers(EveryFrame));

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);
        await transcoders.Raised[0].WriteAsync(Fmp4.Fragment(1_000));

        LiveFrame[] toTheFirst = [await Next(first), await Next(first), await Next(first)];
        LiveFrame[] toTheSecond = [await Next(second), await Next(second), await Next(second)];

        Assert.Equal(
            [LiveChannel.CaptionHeader, LiveChannel.PictureHeader, LiveChannel.Picture],
            toTheFirst.Select(frame => frame.Channel));
        Assert.Equal(
            toTheFirst.Select(frame => frame.Payload.ToArray()),
            toTheSecond.Select(frame => frame.Payload.ToArray()));
    }

    [Fact]
    public async Task AKeyThatDiffersOnlyInFrameRateRaisesATranscoderOfItsOwn()
    {
        await using ILiveViewing frames = await Joined(EveryFrame);
        await using ILiveViewing fields = await Joined(EveryField);

        Assert.Equal(2, transcoders.Started);
        Assert.Equal(2, budget.Running);
        Assert.Equal([LiveProfile.Hd30, LiveProfile.Hd60], transcoders.Raised.Select(raised => raised.Profile));
        Assert.Equal(1, manager.Viewers(EveryFrame));
        Assert.Equal(1, manager.Viewers(EveryField));
    }

    [Fact]
    public async Task EachTranscoderIsToldWhichServicesPictureAndSoundsToTake()
    {
        await using ILiveViewing one = await Joined(EveryFrame);
        await using ILiveViewing another = await Joined(AnotherChannel);

        Assert.Equal(
            [EveryFrame.Service, AnotherChannel.Service],
            transcoders.Raised.Select(raised => raised.Service));
    }

    [Fact]
    public async Task AnotherChannelInTheSameProfileIsAnotherSession()
    {
        await using ILiveViewing one = await Joined(EveryFrame);
        await using ILiveViewing another = await Joined(AnotherChannel);

        Assert.Equal(2, transcoders.Started);
        Assert.Equal(
            [(EveryFrame.Network, EveryFrame.Service), (AnotherChannel.Network, AnotherChannel.Service)],
            supply.Opened.Select(opened => (opened.Network, opened.Service)));
    }

    [Fact]
    public async Task TheLastViewerLeavingLeavesTheSessionStandingUntilTheLingerIsOver()
    {
        ILiveViewing viewing = await Joined(EveryFrame);

        await viewing.DisposeAsync();

        clock.Turn(Linger - TimeSpan.FromMilliseconds(1));

        Assert.False(transcoders.Raised[0].Disposed);
        Assert.Equal(1, budget.Running);
        Assert.Equal([EveryFrame], manager.Keys);

        clock.Turn(TimeSpan.FromMilliseconds(1));

        await Eventually.Happens(
            () => budget.Running is 0 && supply.Opened[0].Disposed,
            "the seat comes back and the stream is let go once the linger is over");

        Assert.True(transcoders.Raised[0].Disposed);
        Assert.Empty(manager.Keys);
    }

    [Fact]
    public async Task AViewerBackWithinTheLingerRidesTheSameSessionAndPaysNoStart()
    {
        ILiveViewing gone = await Joined(EveryFrame);

        await gone.DisposeAsync();

        clock.Turn(Linger / 2);

        await using ILiveViewing back = await Joined(EveryFrame);

        clock.Turn(Linger * 3);

        Assert.Equal(1, transcoders.Started);
        Assert.False(transcoders.Raised[0].Disposed);
        Assert.Equal(1, budget.Running);
        Assert.Equal(0, clock.Pending);
    }

    [Fact]
    public async Task OneViewerLeavingWhileAnotherStaysStartsNoLinger()
    {
        await using ILiveViewing staying = await Joined(EveryFrame);
        ILiveViewing leaving = await Joined(EveryFrame);

        await leaving.DisposeAsync();

        Assert.Equal(0, clock.Pending);

        clock.Turn(Linger * 2);

        Assert.False(transcoders.Raised[0].Disposed);
        Assert.Equal(1, manager.Viewers(EveryFrame));
    }

    [Fact]
    public async Task AFullBudgetRefusesANewKeyWithTheCeilingAndLeavesNothingOfItBehind()
    {
        TranscodeBudget one = new(new TranscodeBudgetSettings { AtOnce = 1 });
        HeldTranscoders scarce = new(one);
        LiveSessionManager crowded = Managing(one, scarce);

        await using ILiveViewing first = Seated(await crowded.JoinAsync(EveryFrame, CancellationToken.None));

        LiveJoin refused = await crowded.JoinAsync(EveryField, CancellationToken.None);

        Assert.False(refused.Seated);
        Assert.Equal(LiveRefusal.TooManyAlready, refused.Refusal);
        Assert.Equal(new TranscodeCeiling(1, 1), refused.Ceiling);
        Assert.Equal(1, scarce.Started);
        Assert.Equal([EveryFrame], crowded.Keys);
        await Eventually.Happens(() => supply.Opened[1].Disposed, "the stream opened for the refused key is let go");
    }

    [Fact]
    public async Task ASupplyThatRefusesIsPassedOnAndRaisesNoTranscoder()
    {
        supply.Refusing = LiveRefusal.NoTunerFree;

        LiveJoin refused = await manager.JoinAsync(EveryFrame, CancellationToken.None);

        Assert.Equal(LiveRefusal.NoTunerFree, refused.Refusal);
        Assert.Equal("held back for the test.", refused.Note);
        Assert.Equal(0, transcoders.Started);
        Assert.Equal(1, supply.Asked);
        Assert.Empty(manager.Keys);
    }

    [Fact]
    public async Task BrPs001AChannelChangeIsNotRefusedWhileTheOneLeftBehindIsStillLingering()
    {
        supply.AsIfThereWereOneTuner = true;

        ILiveViewing watching = await Joined(EveryFrame);

        await watching.DisposeAsync();

        await using ILiveViewing next = await Joined(AnotherChannel);

        Assert.True(supply.Opened[0].Disposed);
        Assert.Equal([AnotherChannel], manager.Keys);
        Assert.Equal(3, supply.Asked);
        Assert.Equal(0, clock.Pending);
    }

    [Fact]
    public async Task BrPs001AChannelSomebodyIsStillWatchingIsNotGivenUpAndTheOneAskingIsRefusedAtOnce()
    {
        supply.AsIfThereWereOneTuner = true;

        await using ILiveViewing watching = await Joined(EveryFrame);

        LiveJoin refused = await manager.JoinAsync(AnotherChannel, CancellationToken.None);

        Assert.Equal(LiveRefusal.NoTunerFree, refused.Refusal);
        Assert.False(supply.Opened[0].Disposed);
        Assert.Equal([EveryFrame], manager.Keys);
        Assert.Equal(2, supply.Asked);
    }

    [Fact]
    public async Task BrPs001ATunerHeldBySomethingThatIsNotLiveIsRefusedAtOnceWithNothingGivenUp()
    {
        supply.AsIfThereWereOneTuner = true;

        await using ILiveViewing watching = await Joined(EveryFrame);

        supply.Refusing = LiveRefusal.NoTunerFree;

        LiveJoin refused = await manager.JoinAsync(AnotherChannel, CancellationToken.None);

        Assert.Equal(LiveRefusal.NoTunerFree, refused.Refusal);
        Assert.Equal("held back for the test.", refused.Note);
        Assert.False(supply.Opened[0].Disposed);
        Assert.Equal(2, supply.Asked);
    }

    [Fact]
    public async Task BrPs001TheLedgerShowsOnlyTheChannelLeftAfterTheOtherIsGivenUp()
    {
        supply.AsIfThereWereOneTuner = true;

        ILiveViewing watching = await Joined(EveryFrame);

        await watching.DisposeAsync();

        await using ILiveViewing next = await Joined(AnotherChannel);

        Assert.Equal([AnotherChannel], Ledger.Running.Select(view => view.Key));
        Assert.All(events.Signalled, name => Assert.Same(AppEventName.Live, name));
    }

    [Fact]
    public async Task BrPs001AViewerWaitingLongerThanTheLongestRaiseIsRefusedRatherThanLeftWaiting()
    {
        supply.HeldUntil = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<LiveJoin> joining = manager.JoinAsync(EveryFrame, CancellationToken.None);

        await Eventually.Happens(() => supply.Asked is 1, "the viewer reaches the supply");

        clock.Turn(LongestRaise);

        LiveJoin refused = await joining;

        Assert.Equal(LiveRefusal.DriverUnavailable, refused.Refusal);
        Assert.Contains("no transport stream was opened", refused.Note, StringComparison.Ordinal);
        await Eventually.Happens(() => manager.Keys.Count is 0, "the session nothing came of is forgotten");

        supply.HeldUntil.SetResult();
    }

    [Fact]
    public async Task BrPs001AViewerWaitingOnATranscoderThatNeverStartsIsToldTheTunerWasSecured()
    {
        transcoders.HeldUntil = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<LiveJoin> joining = manager.JoinAsync(EveryFrame, CancellationToken.None);

        await Eventually.Happens(() => supply.Opened.Count is 1, "the viewer secures the tuner");

        clock.Turn(LongestRaise);

        LiveJoin refused = await joining;

        Assert.Equal(LiveRefusal.TranscoderWouldNotStart, refused.Refusal);
        Assert.Contains("the tuner was secured", refused.Note, StringComparison.Ordinal);
        await Eventually.Happens(() => supply.Opened[0].Disposed, "the stream is let go with the session nothing came of");

        Assert.Empty(manager.Keys);

        transcoders.HeldUntil.SetResult();
    }

    [Fact]
    public async Task BrPs001AViewerSeatedWellWithinTheLongestRaiseLeavesNoDeadlineBehind()
    {
        await using ILiveViewing viewing = await Joined(EveryFrame);

        Assert.Equal(0, clock.Pending);
    }

    [Fact]
    public async Task ATranscoderThatWillNotStartIsPassedOnAndTheSupplyIsLetGo()
    {
        transcoders.Failing = TranscoderFault.ProgrammeMissing;

        LiveJoin refused = await manager.JoinAsync(EveryFrame, CancellationToken.None);

        Assert.Equal(LiveRefusal.TranscoderWouldNotStart, refused.Refusal);
        Assert.Equal(0, budget.Running);
        Assert.Empty(manager.Keys);
        await Eventually.Happens(() => supply.Opened[0].Disposed, "the stream is let go when nothing transcodes it");
    }

    [Fact]
    public async Task ASessionThatWasRefusedIsTriedAfreshByTheNextViewer()
    {
        supply.Refusing = LiveRefusal.NoTunerFree;

        Assert.Equal(LiveRefusal.NoTunerFree, (await manager.JoinAsync(EveryFrame, CancellationToken.None)).Refusal);

        supply.Refusing = null;

        await using ILiveViewing seated = await Joined(EveryFrame);

        Assert.Equal(2, supply.Asked);
        Assert.Equal(1, transcoders.Started);
    }

    [Fact]
    public async Task ViewersArrivingTogetherRaiseOneTranscoderBetweenThem()
    {
        const int arriving = 8;

        supply.HeldUntil = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using Barrier together = new(arriving);

        Task<LiveJoin>[] joining =
        [
            .. Enumerable.Range(0, arriving).Select(_ => Task.Run(() =>
            {
                together.SignalAndWait();

                return manager.JoinAsync(EveryFrame, CancellationToken.None);
            })),
        ];

        await Eventually.Happens(() => supply.Asked >= 1, "the first arrival reaches the supply");

        supply.HeldUntil.SetResult();

        LiveJoin[] joined = await Task.WhenAll(joining);

        Assert.All(joined, join => Assert.True(join.Seated, join.Note));
        Assert.Equal(1, supply.Asked);
        Assert.Equal(1, transcoders.Started);
        Assert.Equal(1, budget.Running);
        Assert.Equal(arriving, manager.Viewers(EveryFrame));

        foreach (LiveJoin join in joined)
        {
            await join.Viewing!.DisposeAsync();
        }
    }

    [Fact]
    public async Task TheTranscoderEndingEndsTheSessionForItsViewersAndGivesTheSeatBack()
    {
        await using ILiveViewing viewing = await Joined(EveryFrame);

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);
        transcoders.Raised[0].NoMore();

        Assert.Equal(LiveChannel.CaptionHeader, (await Next(viewing)).Channel);
        Assert.Equal(LiveChannel.PictureHeader, (await Next(viewing)).Channel);
        await viewing.Frames.Completion.WaitAsync(Eventually.Patience);
        await Eventually.Happens(
            () => budget.Running is 0 && supply.Opened[0].Disposed,
            "the seat comes back and the stream is let go when the transcoder ends");

        Assert.Empty(manager.Keys);

        await using ILiveViewing afresh = await Joined(EveryFrame);

        Assert.Equal(2, transcoders.Started);
    }

    [Fact]
    public async Task AJoinerThatGivesUpWhileTheSupplyIsStillOpeningLeavesNoSessionBehind()
    {
        supply.HeldUntil = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenSource givingUp = new();

        Task<LiveJoin> joining = manager.JoinAsync(EveryFrame, givingUp.Token);

        await Eventually.Happens(() => supply.Asked is 1, "the joiner reaches the supply");
        await givingUp.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => joining);

        clock.Turn(Linger);

        await Eventually.Happens(() => manager.Keys.Count is 0, "the session nobody waits for is forgotten");

        supply.HeldUntil.SetResult();

        Assert.Equal(0, transcoders.Started);
        Assert.Empty(supply.Opened);
    }

    [Fact]
    public async Task TheSupplyIsLetGoEvenWhenTheStoppedTranscoderNoLongerHandsOutItsOutput()
    {
        ILiveViewing viewing = await Joined(EveryFrame);

        transcoders.Raised[0].NoMore();

        await Eventually.Happens(() => supply.Opened[0].Disposed, "the stream is let go after the transcoder ended on its own");

        Assert.Throws<ObjectDisposedException>(() => transcoders.Raised[0].Output);
        Assert.Empty(manager.Keys);

        await viewing.DisposeAsync();
    }

    [Fact]
    public async Task TheSupplyIsLetGoEvenWhenTheTranscoderWillNotStopCleanly()
    {
        ILiveViewing viewing = await Joined(EveryFrame);

        transcoders.Raised[0].FailingToStop = new InvalidOperationException("the transcoder would not stop.");

        await viewing.DisposeAsync();

        clock.Turn(Linger);

        await Eventually.Happens(() => supply.Opened[0].Disposed, "the stream is let go although the transcoder failed to stop");

        Assert.Equal(0, budget.Running);
        Assert.Empty(manager.Keys);
    }

    [Fact]
    public async Task TheSupplyAndTheTranscoderAreLetGoEvenWhenTheCaptionerWillNotStopCleanly()
    {
        ILiveViewing viewing = await Joined(EveryFrame);

        captioners.Raised[0].FailingToStop = new InvalidOperationException("the captioner would not stop.");

        await viewing.DisposeAsync();

        clock.Turn(Linger);

        await Eventually.Happens(
            () => supply.Opened[0].Disposed && transcoders.Raised[0].Disposed,
            "the stream and the transcoder are let go although the captioner failed to stop");

        Assert.Equal(0, budget.Running);
        Assert.Empty(manager.Keys);
    }

    [Fact]
    public async Task AViewerBackWithinTheLingerWhoLeavesAgainLetsTheSupplyGoOnceAfterTheSecondLinger()
    {
        ILiveViewing gone = await Joined(EveryFrame);

        await gone.DisposeAsync();

        clock.Turn(Linger / 2);

        ILiveViewing back = await Joined(EveryFrame);

        clock.Turn(Linger);

        Assert.False(supply.Opened[0].Disposed);

        await back.DisposeAsync();

        clock.Turn(Linger);

        await Eventually.Happens(() => supply.Opened[0].Disposed, "the stream is let go once the second linger is over");

        clock.Turn(Linger * 2);

        Assert.Equal(1, supply.Asked);
        Assert.Equal(1, supply.Opened[0].TimesLetGo);
        Assert.Equal(0, clock.Pending);
        Assert.Empty(manager.Keys);
    }

    [Fact]
    public async Task ASessionMarksItsOwnStartupAsTheTranscoderTheHeaderAndTheFirstPictureArrive()
    {
        await using ILiveViewing viewing = await Joined(EveryFrame);

        LiveStartup raised = viewing.Startup!.Current!;

        Assert.True(raised.Reached(LiveStartupSegment.TranscoderStarted));
        Assert.False(raised.Reached(LiveStartupSegment.InitReached));
        Assert.True(raised.InProgress);

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);
        await Next(viewing);
        await Next(viewing);
        await Eventually.Happens(
            () => viewing.Startup.Current!.Reached(LiveStartupSegment.InitReached),
            "the header reaching the fanout is marked");

        Assert.False(viewing.Startup.Current!.Reached(LiveStartupSegment.FirstPicture));

        await transcoders.Raised[0].WriteAsync(Fmp4.Fragment(1_000));
        await Next(viewing);
        await Eventually.Happens(
            () => !viewing.Startup.Current!.InProgress,
            "the first picture reaching the fanout ends the startup");

        LiveStartup done = viewing.Startup.Current!;

        Assert.True(done.At(LiveStartupSegment.TranscoderStarted) <= done.At(LiveStartupSegment.InitReached));
        Assert.True(done.At(LiveStartupSegment.InitReached) <= done.At(LiveStartupSegment.FirstPicture));
        Assert.True(done.Reached(LiveStartupSegment.TunerSecured));
        Assert.False(done.Reached(LiveStartupSegment.ChannelLocked));
    }

    [Fact]
    public async Task ASessionMarksTheTunerSecuredWhenTheSupplyOpensAndTheChannelLockedWhenItsFirstBytesArrive()
    {
        await using ILiveViewing viewing = await Joined(EveryFrame);

        LiveStartup raised = viewing.Startup!.Current!;

        Assert.True(raised.Reached(LiveStartupSegment.TunerSecured));
        Assert.True(raised.At(LiveStartupSegment.TunerSecured) <= raised.At(LiveStartupSegment.TranscoderStarted));
        Assert.False(raised.Reached(LiveStartupSegment.ChannelLocked));

        await supply.Opened[0].WriteAsync(new byte[1_000]);
        await Eventually.Happens(
            () => viewing.Startup.Current!.Reached(LiveStartupSegment.ChannelLocked),
            "the first bytes from the supply mark the channel as locked");

        Assert.Null(viewing.Ending!.Current);
    }

    [Fact]
    public async Task ASupplyThatIsRefusedMarksNoSegmentAtAll()
    {
        supply.Refusing = LiveRefusal.WouldNotTune;

        await manager.JoinAsync(EveryFrame, CancellationToken.None);

        Assert.Null(manager.Startup(EveryFrame));
    }

    [Fact]
    public async Task WhyTheSupplyEndedReachesEveryViewerBeforeTheirFramesEnd()
    {
        await using ILiveViewing first = await Joined(EveryFrame);
        await using ILiveViewing second = await Joined(EveryFrame);

        supply.Opened[0].Ending = LiveSupplyEnding.Of(LiveSupplyEnd.TakenForARecording, "a recording outranked it.");
        supply.Opened[0].NoMore();

        await Eventually.Happens(() => first.Ending!.Current is not null, "the ending is noted when the supply ends");

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);
        transcoders.Raised[0].NoMore();

        Assert.Equal(LiveChannel.CaptionHeader, (await Next(first)).Channel);
        Assert.Equal(LiveChannel.PictureHeader, (await Next(first)).Channel);
        Assert.Equal(LiveChannel.CaptionHeader, (await Next(second)).Channel);
        Assert.Equal(LiveChannel.PictureHeader, (await Next(second)).Channel);
        await first.Frames.Completion.WaitAsync(Eventually.Patience);
        await second.Frames.Completion.WaitAsync(Eventually.Patience);

        Assert.Same(first.Ending, second.Ending);
        Assert.Equal(LiveSupplyEnd.TakenForARecording, first.Ending!.Current!.Why);
        Assert.Equal("a recording outranked it.", first.Ending.Current.Note);
    }

    [Fact]
    public async Task ASupplyThatEndsWithoutAWordIsSaidToHaveBeenLost()
    {
        await using ILiveViewing viewing = await Joined(EveryFrame);

        supply.Opened[0].NoMore();

        await Eventually.Happens(() => viewing.Ending!.Current is not null, "the ending is noted when the supply ends");

        Assert.Equal(LiveSupplyEnd.DriverLost, viewing.Ending!.Current!.Why);
    }

    [Fact]
    public async Task ASessionTornDownByItsOwnLingerNotesNoEnding()
    {
        ILiveViewing viewing = await Joined(EveryFrame);

        await viewing.DisposeAsync();

        clock.Turn(Linger);

        await Eventually.Happens(() => supply.Opened[0].Disposed, "the stream is let go once the linger is over");

        Assert.Null(viewing.Ending!.Current);
    }

    [Fact]
    public async Task EveryViewerOfOneSessionReadsTheSameStartup()
    {
        await using ILiveViewing first = await Joined(EveryFrame);
        await using ILiveViewing second = await Joined(EveryFrame);
        await using ILiveViewing elsewhere = await Joined(EveryField);

        Assert.Same(first.Startup, second.Startup);
        Assert.NotSame(first.Startup, elsewhere.Startup);
    }

    [Fact]
    public async Task DisposingTheManagerTearsEverySessionDown()
    {
        await using ILiveViewing one = await Joined(EveryFrame);
        await using ILiveViewing another = await Joined(EveryField);

        await manager.DisposeAsync();

        Assert.Equal(0, budget.Running);
        Assert.All(transcoders.Raised, raised => Assert.True(raised.Disposed));
        Assert.All(supply.Opened, opened => Assert.True(opened.Disposed));
        Assert.Empty(manager.Keys);
    }

    [Fact]
    public async Task TheLedgerNamesEveryRunningSessionWithItsViewersItsStartupAndWhatItThrewAway()
    {
        await using ILiveViewing first = await Joined(EveryFrame);
        await using ILiveViewing second = await Joined(EveryFrame);
        await using ILiveViewing other = await Joined(AnotherChannel);

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);
        await transcoders.Raised[0].WriteAsync(Fmp4.Fragment(1_000));
        await Eventually.Happens(
            () => manager.Startup(EveryFrame)?.Current is { InProgress: false },
            "the first picture ends the startup");

        LiveSessionView[] running = [.. Ledger.Running.OrderBy(view => view.Key.ToString(), StringComparer.Ordinal)];

        Assert.Equal([EveryFrame, AnotherChannel], running.Select(view => view.Key));
        Assert.Equal([2, 1], running.Select(view => view.Viewers));
        Assert.False(running[0].Startup.InProgress);
        Assert.True(running[1].Startup.InProgress);
        Assert.Equal([0L, 0L], running.Select(view => view.Dropped));
    }

    [Fact]
    public async Task TheLedgerCountsWhatASlowViewerLost()
    {
        LiveSessionManager crowded = new(
            new LiveSessionSettings { Linger = Linger, LongestRaise = LongestRaise },
            new LiveFanoutSettings { LongestBacklog = 1 },
            supply,
            transcoders,
            captioners,
            clock,
            events);

        await using ILiveViewing slow = Seated(await crowded.JoinAsync(EveryFrame, CancellationToken.None));

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);

        for (int fragment = 1; fragment <= 4; fragment++)
        {
            await transcoders.Raised[0].WriteAsync(Fmp4.Fragment(fragment * 1_000));
        }

        await Eventually.Happens(() => slow.Backlog.Dropped >= 2L, "pictures beyond the backlog are thrown away");

        Assert.Equal(slow.Backlog.Dropped, ((ILiveSessionLedger)crowded).Running.Single().Dropped);

        await crowded.DisposeAsync();
    }

    [Fact]
    public async Task ASessionRaisesOneCaptionerBesideItsTranscoderForTheSameServiceAndPicture()
    {
        await using ILiveViewing first = await Joined(EveryFrame);
        await using ILiveViewing second = await Joined(EveryFrame);
        await using ILiveViewing other = await Joined(AnotherChannel);

        Assert.Equal(2, captioners.Started);
        Assert.Equal([EveryFrame.Service, AnotherChannel.Service], captioners.Raised.Select(raised => raised.Service));
        Assert.Equal(transcoders.Raised[0].Attributes, captioners.Raised[0].Attributes);
        Assert.Equal(2, transcoders.Started);
        Assert.Equal(2, budget.Running);
    }

    [Fact]
    public async Task TheCaptionCanvasIsHandedToEveryViewerWithTheHeaders()
    {
        await using ILiveViewing viewing = await Joined(EveryFrame);

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);

        LiveFrame[] handed = [await Next(viewing), await Next(viewing)];

        Assert.Equal([LiveChannel.PictureHeader, LiveChannel.CaptionHeader], handed.Select(frame => frame.Channel).Order());
        Assert.Equal(
            transcoders.Raised[0].Attributes.Size,
            LiveCaptions.CanvasOf(handed.Single(frame => frame.Channel is LiveChannel.CaptionHeader)));
    }

    [Fact]
    public async Task WhatTheCaptionerDrawsReachesEveryViewerAndALateOneIsHandedWhatIsShowing()
    {
        await using ILiveViewing early = await Joined(EveryFrame);

        Assert.Equal(LiveChannel.CaptionHeader, (await Next(early)).Channel);

        LiveFrame shown = LiveCaptions.Shown(LivePts.Of(90_000UL), new CaptionPicture(1, 2, 3, 4, new byte[] { 0x89, 0x50 }));

        captioners.Raised[0].Draw(shown);

        LiveFrame toEarly = await Next(early);

        Assert.Equal(LiveChannel.Caption, toEarly.Channel);
        Assert.Equal(shown.Payload.ToArray(), toEarly.Payload.ToArray());

        await using ILiveViewing late = await Joined(EveryFrame);

        LiveFrame[] toLate = [await Next(late), await Next(late)];

        Assert.Equal([LiveChannel.CaptionHeader, LiveChannel.Caption], toLate.Select(frame => frame.Channel));
        Assert.Equal(90_000UL, toLate[1].Pts.Value);

        captioners.Raised[0].Draw(LiveCaptions.Cleared(LivePts.Of(180_000UL)));

        Assert.True(LiveCaptions.Clears(await Next(early)));
        Assert.True(LiveCaptions.Clears(await Next(late)));

        await using ILiveViewing later = await Joined(EveryFrame);

        Assert.Equal(LiveChannel.CaptionHeader, (await Next(later)).Channel);
        Assert.False(later.Frames.TryRead(out _));
    }

    [Fact]
    public async Task TheBytesFedToTheTranscoderAreFedToTheCaptionerToo()
    {
        await using ILiveViewing viewing = await Joined(EveryFrame);

        await supply.Opened[0].WriteAsync([1, 2, 3, 4, 5]);

        byte[] heard = new byte[5];
        int read = 0;

        while (read < heard.Length)
        {
            using CancellationTokenSource patience = new(Eventually.Patience);

            read += await captioners.Raised[0].Fed.ReadAsync(heard.AsMemory(read), patience.Token);
        }

        Assert.Equal([1, 2, 3, 4, 5], heard);
    }

    [Fact]
    public async Task ACaptionerThatWillNotStartCostsTheViewerNothingButCaptions()
    {
        captioners.Failing = TranscoderFault.ProgrammeMissing;

        await using ILiveViewing viewing = await Joined(EveryFrame);

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);
        await transcoders.Raised[0].WriteAsync(Fmp4.Fragment(1_000));

        Assert.Equal([LiveChannel.PictureHeader, LiveChannel.Picture], new[] { await Next(viewing), await Next(viewing) }.Select(frame => frame.Channel));
        Assert.Equal(0, captioners.Started);
        Assert.Equal(1, manager.Viewers(EveryFrame));
    }

    [Fact]
    public async Task ACaptionerThatEndsOnItsOwnEndsNothingElse()
    {
        await using ILiveViewing viewing = await Joined(EveryFrame);

        captioners.Raised[0].NoMore();

        await transcoders.Raised[0].WriteAsync(Fmp4.Header);
        await transcoders.Raised[0].WriteAsync(Fmp4.Fragment(1_000));

        LiveFrame[] handed = [await Next(viewing), await Next(viewing), await Next(viewing)];

        Assert.Contains(LiveChannel.Picture, handed.Select(frame => frame.Channel));
        Assert.Equal(1, manager.Viewers(EveryFrame));
        Assert.False(viewing.Frames.Completion.IsCompleted);
    }

    [Fact]
    public async Task TheCaptionerIsTornDownWithTheSession()
    {
        ILiveViewing viewing = await Joined(EveryFrame);

        await viewing.DisposeAsync();

        clock.Turn(Linger);

        await Eventually.Happens(() => captioners.Raised[0].Disposed, "the captioner is let go with the transcoder");

        Assert.True(transcoders.Raised[0].Disposed);
    }

    [Fact]
    public async Task DisposingTheManagerTearsEveryCaptionerDownToo()
    {
        await using ILiveViewing one = await Joined(EveryFrame);
        await using ILiveViewing another = await Joined(EveryField);

        await manager.DisposeAsync();

        Assert.All(captioners.Raised, raised => Assert.True(raised.Disposed));
    }

    [Fact]
    public async Task RaisingASessionSignalsLiveOnceHoweverManyViewersRideIt()
    {
        await using ILiveViewing first = await Joined(EveryFrame);
        await using ILiveViewing second = await Joined(EveryFrame);

        Assert.Equal([AppEventName.Live], events.Signalled);
    }

    [Fact]
    public async Task EverySessionRaisedIsSignalledAndNothingButLiveIsEverSignalled()
    {
        await using ILiveViewing one = await Joined(EveryFrame);
        await using ILiveViewing another = await Joined(AnotherChannel);

        Assert.Equal(2, events.Signalled.Count);
        Assert.All(events.Signalled, name => Assert.Same(AppEventName.Live, name));
    }

    [Fact]
    public async Task TheSessionGoingAwayAfterItsLingerSignalsLiveAgain()
    {
        ILiveViewing viewing = await Joined(EveryFrame);

        await viewing.DisposeAsync();

        Assert.Equal([AppEventName.Live], events.Signalled);

        clock.Turn(Linger);

        await Eventually.Happens(() => events.Signalled.Count is 2, "the session being torn down is signalled");

        Assert.Empty(manager.Keys);
        Assert.All(events.Signalled, name => Assert.Same(AppEventName.Live, name));
    }

    [Fact]
    public async Task AViewerBackWithinTheLingerSignalsNothingBecauseTheSessionNeverWentAway()
    {
        ILiveViewing gone = await Joined(EveryFrame);

        await gone.DisposeAsync();

        clock.Turn(Linger / 2);

        await using ILiveViewing back = await Joined(EveryFrame);

        Assert.Equal([AppEventName.Live], events.Signalled);
    }

    [Fact]
    public async Task ASessionTheSupplyRefusesIsSignalledComingAndGoingSoAReaderSeesTheLedgerAsItWas()
    {
        supply.Refusing = LiveRefusal.NoTunerFree;

        await manager.JoinAsync(EveryFrame, CancellationToken.None);

        await Eventually.Happens(() => events.Signalled.Count is 2, "the refused session is signalled in and out");

        Assert.Empty(manager.Keys);
    }

    [Fact]
    public void TheLedgerIsEmptyWhileNothingIsBeingSentLive()
    {
        Assert.Empty(Ledger.Running);
    }

    [Fact]
    public void TheOnlyThingAViewerCanAskOfTheManagerIsToJoinAKey()
    {
        MethodInfo[] asked =
        [
            .. typeof(LiveSessionManager)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName),
        ];

        Assert.Equal(["DisposeAsync", "JoinAsync", "Startup", "Viewers"], asked.Select(method => method.Name).Order());
        Assert.All(
            asked.SelectMany(method => method.GetParameters()),
            parameter => Assert.DoesNotContain(
                parameter.ParameterType,
                (Type[])[typeof(NetworkId), typeof(ServiceId), typeof(TuningParameters), typeof(LiveProfile)]));
        Assert.Equal([nameof(ILiveSessionManager.JoinAsync)], typeof(ILiveSessionManager).GetMethods().Select(method => method.Name));
    }

    private static ILiveViewing Seated(LiveJoin join)
    {
        Assert.True(join.Seated, join.Note);

        return join.Viewing!;
    }

    private static async Task<LiveFrame> Next(ILiveViewing viewing)
    {
        using CancellationTokenSource patience = new(Eventually.Patience);

        return await viewing.Frames.ReadAsync(patience.Token);
    }

    private LiveSessionManager Managing(TranscodeBudget counting, HeldTranscoders? raising = null)
        => new(
            new LiveSessionSettings { Linger = Linger, LongestRaise = LongestRaise },
            new LiveFanoutSettings(),
            supply,
            raising ?? new HeldTranscoders(counting),
            captioners,
            clock,
            events);

    private async Task<ILiveViewing> Joined(LiveSessionKey key)
        => Seated(await manager.JoinAsync(key, CancellationToken.None));
}
