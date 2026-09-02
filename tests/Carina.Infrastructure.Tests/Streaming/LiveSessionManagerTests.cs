using System.Reflection;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class LiveSessionManagerTests
{
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(5);

    private static readonly LiveSessionKey EveryFrame = new(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd30);

    private static readonly LiveSessionKey EveryField = new(new NetworkId(32736), new ServiceId(1024), LiveProfile.Hd60);

    private static readonly LiveSessionKey AnotherChannel = new(new NetworkId(32736), new ServiceId(1032), LiveProfile.Hd30);

    private readonly HandTurnedClock clock = new();

    private readonly PipedSupply supply = new();

    private readonly TranscodeBudget budget = new(new TranscodeBudgetSettings { AtOnce = 4 });

    private readonly HeldTranscoders transcoders;

    private readonly LiveSessionManager manager;

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

        LiveFrame[] toTheFirst = [await Next(first), await Next(first)];
        LiveFrame[] toTheSecond = [await Next(second), await Next(second)];

        Assert.Equal([LiveChannel.PictureHeader, LiveChannel.Picture], toTheFirst.Select(frame => frame.Channel));
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

        await Eventually.Happens(() => budget.Running is 0, "the seat comes back once the linger is over");

        Assert.True(transcoders.Raised[0].Disposed);
        Assert.True(supply.Opened[0].Disposed);
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
        Assert.Empty(manager.Keys);
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

        Assert.Equal(LiveChannel.PictureHeader, (await Next(viewing)).Channel);
        await viewing.Frames.Completion.WaitAsync(Eventually.Patience);
        await Eventually.Happens(() => budget.Running is 0, "the seat comes back when the transcoder ends");

        Assert.Empty(manager.Keys);
        Assert.True(supply.Opened[0].Disposed);

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
    public void TheOnlyThingAViewerCanAskOfTheManagerIsToJoinAKey()
    {
        MethodInfo[] asked =
        [
            .. typeof(LiveSessionManager)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName),
        ];

        Assert.Equal(["DisposeAsync", "JoinAsync", "Viewers"], asked.Select(method => method.Name).Order());
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
            new LiveSessionSettings { Linger = Linger },
            new LiveFanoutSettings(),
            supply,
            raising ?? new HeldTranscoders(counting),
            clock);

    private async Task<ILiveViewing> Joined(LiveSessionKey key)
        => Seated(await manager.JoinAsync(key, CancellationToken.None));
}
