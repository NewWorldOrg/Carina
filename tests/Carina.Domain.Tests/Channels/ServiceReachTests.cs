using Carina.Contracts;
using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class ServiceReachTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    [Fact]
    public void ASystemNothingHasEverBeenFoundOnIsUnmeasuredRatherThanMissing()
    {
        SystemReach reach = Only(Assess([TuneSystem.IsdbSBs], []));

        Assert.Equal(ServiceReachLevel.Unmeasured, reach.Level);
        Assert.Equal(0, reach.Services);
        Assert.Null(reach.LastSeenAt);
    }

    [Fact]
    public void ASystemWithServicesInRotationIsReaching()
    {
        SystemReach reach = Only(Assess([TuneSystem.IsdbT], [InRotation(101, Now), InRotation(102, Now)]));

        Assert.Equal(ServiceReachLevel.Reaching, reach.Level);
        Assert.Equal(2, reach.Services);
    }

    [Fact]
    public void OneServiceFoundOnTwoChannelsIsCountedOnce()
    {
        SystemReach reach = Only(Assess(
            [TuneSystem.IsdbT],
            [InRotation(101, Now, 27), InRotation(101, Now, 28)]));

        Assert.Equal(1, reach.Services);
    }

    [Fact]
    public void ASystemSilentForLongerThanAllowedIsMissing()
    {
        SystemReach reach = Only(Assess(
            [TuneSystem.IsdbSBs],
            [Abandoned(101, Now - Day - TimeSpan.FromTicks(1))]));

        Assert.Equal(ServiceReachLevel.Missing, reach.Level);
        Assert.Equal(0, reach.Services);
    }

    [Fact]
    public void ASystemSilentForExactlyAsLongAsAllowedIsMissing()
    {
        Assert.Equal(
            ServiceReachLevel.Missing,
            Only(Assess([TuneSystem.IsdbSBs], [Abandoned(101, Now - Day)])).Level);
    }

    [Fact]
    public void ASystemSilentForOneTickLessThanAllowedIsOnlySilent()
    {
        SystemReach reach = Only(Assess(
            [TuneSystem.IsdbSBs],
            [Abandoned(101, Now - Day + TimeSpan.FromTicks(1))]));

        Assert.Equal(ServiceReachLevel.Silent, reach.Level);
        Assert.Equal(0, reach.Services);
    }

    [Fact]
    public void TheMostRecentSightingIsTheOneTheSilenceIsMeasuredFrom()
    {
        SystemReach reach = Only(Assess(
            [TuneSystem.IsdbSBs],
            [Abandoned(101, Now - TimeSpan.FromDays(9)), Abandoned(102, Now - TimeSpan.FromHours(1))]));

        Assert.Equal(ServiceReachLevel.Silent, reach.Level);
        Assert.Equal(Now - TimeSpan.FromHours(1), reach.LastSeenAt);
    }

    [Fact]
    public void ASystemNoTunerServesIsNotAssessedAtAll()
    {
        IReadOnlyList<SystemReach> assessed = Assess(
            [TuneSystem.IsdbT],
            [Abandoned(101, Now - TimeSpan.FromDays(9), 27, TuneSystem.IsdbSBs)]);

        Assert.Equal(TuneSystem.IsdbT, Only(assessed).System);
        Assert.Equal(ServiceReachLevel.Unmeasured, Only(assessed).Level);
    }

    [Fact]
    public void EverySystemAskedAboutIsAnsweredOnceAndInOrder()
    {
        IReadOnlyList<SystemReach> assessed = Assess(
            [TuneSystem.IsdbSCs110, TuneSystem.IsdbT, TuneSystem.IsdbSCs110],
            []);

        Assert.Equal([TuneSystem.IsdbT, TuneSystem.IsdbSCs110], assessed.Select(reach => reach.System));
    }

    [Fact]
    public void AMachineWithATunerNobodyCouldDescribeIsJudgedOnEverySystem()
    {
        IReadOnlyList<SystemReach> assessed = Assess([], [], undescribedTuners: true);

        Assert.Equal(
            [TuneSystem.IsdbT, TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110],
            assessed.Select(reach => reach.System));
        Assert.All(assessed, reach => Assert.Equal(ServiceReachLevel.Undetermined, reach.Level));
    }

    [Fact]
    public void ATunerNobodyCouldDescribeDoesNotTurnAKnownSystemIntoAnUnknownOne()
    {
        IReadOnlyList<SystemReach> assessed = Assess([TuneSystem.IsdbT], [], undescribedTuners: true);

        Assert.Equal(
            ServiceReachLevel.Unmeasured,
            assessed.Single(reach => reach.System is TuneSystem.IsdbT).Level);
        Assert.Equal(
            ServiceReachLevel.Undetermined,
            assessed.Single(reach => reach.System is TuneSystem.IsdbSBs).Level);
    }

    [Fact]
    public void EvidenceStillDecidesEvenWhenATunerCouldNotBeDescribed()
    {
        IReadOnlyList<SystemReach> assessed = Assess(
            [],
            [Abandoned(101, Now - TimeSpan.FromDays(9))],
            undescribedTuners: true);

        Assert.Equal(
            ServiceReachLevel.Missing,
            assessed.Single(reach => reach.System is TuneSystem.IsdbSBs).Level);
        Assert.Equal(
            ServiceReachLevel.Undetermined,
            assessed.Single(reach => reach.System is TuneSystem.IsdbT).Level);
    }

    [Fact]
    public void ASilenceOfNoTimeAtAllIsRefused()
    {
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => ServiceReach.Assess([TuneSystem.IsdbT], false, [], TimeSpan.Zero, Now));

        Assert.Equal("silence", thrown.ParamName);
    }

    [Fact]
    public void AnAssessmentOfNothingAtAllIsRefused()
    {
        Assert.Equal(
            "served",
            Assert.Throws<ArgumentNullException>(
                () => ServiceReach.Assess(null!, false, [], Day, Now)).ParamName);
        Assert.Equal(
            "candidates",
            Assert.Throws<ArgumentNullException>(
                () => ServiceReach.Assess([], false, null!, Day, Now)).ParamName);
    }

    private static IReadOnlyList<SystemReach> Assess(
        IReadOnlyList<TuneSystem> served,
        IReadOnlyList<CandidateChannel> candidates,
        bool undescribedTuners = false)
        => ServiceReach.Assess(served, undescribedTuners, candidates, Day, Now);

    private static SystemReach Only(IReadOnlyList<SystemReach> assessed) => Assert.Single(assessed);

    private static CandidateChannel InRotation(
        int service,
        DateTime lastSeenAt,
        int physicalChannel = 27,
        TuneSystem system = TuneSystem.IsdbT)
        => Held(service, lastSeenAt, physicalChannel, system, RotationState.Active);

    private static CandidateChannel Abandoned(
        int service,
        DateTime lastSeenAt,
        int physicalChannel = 27,
        TuneSystem system = TuneSystem.IsdbSBs)
        => Held(service, lastSeenAt, physicalChannel, system, RotationState.NeedsAttention);

    private static CandidateChannel Held(
        int service,
        DateTime lastSeenAt,
        int physicalChannel,
        TuneSystem system,
        RotationState state)
        => CandidateChannel.Rehydrate(
            CandidateChannelId.New(),
            new NetworkId(1),
            new ServiceId(service),
            Tuning(system, physicalChannel),
            observedStreamId: null,
            isSelected: false,
            selectionSource: null,
            selectedAt: null,
            selectionMeasurement: null,
            lastMeasurement: null,
            needsRevalidation: false,
            rotationState: state,
            consecutiveFailures: state is RotationState.NeedsAttention ? 5 : 0,
            nextAttemptAt: null,
            needsAttentionSince: state is RotationState.NeedsAttention ? lastSeenAt : null,
            discoveredAt: lastSeenAt,
            lastSeenAt: lastSeenAt);

    private static TuningParameters Tuning(TuneSystem system, int physicalChannel) => system switch
    {
        TuneSystem.IsdbT => TuningParameters.Terrestrial(physicalChannel),
        TuneSystem.IsdbSBs => TuningParameters.Bs(15, new TransportStreamId(physicalChannel)),
        _ => TuningParameters.Cs110(24),
    };
}
