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
        SystemReach reach = Only(ServiceReach.Assess([TuneSystem.IsdbSBs], [], Day, Now));

        Assert.Equal(ServiceReachLevel.Unmeasured, reach.Level);
        Assert.Equal(0, reach.Services);
        Assert.Null(reach.LastSeenAt);
    }

    [Fact]
    public void ASystemWithServicesInRotationIsReaching()
    {
        SystemReach reach = Only(ServiceReach.Assess(
            [TuneSystem.IsdbT],
            [InRotation(101, Now), InRotation(102, Now)],
            Day,
            Now));

        Assert.Equal(ServiceReachLevel.Reaching, reach.Level);
        Assert.Equal(2, reach.Services);
    }

    [Fact]
    public void OneServiceFoundOnTwoChannelsIsCountedOnce()
    {
        SystemReach reach = Only(ServiceReach.Assess(
            [TuneSystem.IsdbT],
            [InRotation(101, Now, 27), InRotation(101, Now, 28)],
            Day,
            Now));

        Assert.Equal(1, reach.Services);
    }

    [Fact]
    public void ASystemSilentForLongerThanAllowedIsMissing()
    {
        SystemReach reach = Only(ServiceReach.Assess(
            [TuneSystem.IsdbSBs],
            [Abandoned(101, Now - Day - TimeSpan.FromTicks(1))],
            Day,
            Now));

        Assert.Equal(ServiceReachLevel.Missing, reach.Level);
        Assert.Equal(0, reach.Services);
    }

    [Fact]
    public void ASystemSilentForExactlyAsLongAsAllowedIsMissing()
    {
        SystemReach reach = Only(ServiceReach.Assess(
            [TuneSystem.IsdbSBs],
            [Abandoned(101, Now - Day)],
            Day,
            Now));

        Assert.Equal(ServiceReachLevel.Missing, reach.Level);
    }

    [Fact]
    public void ASystemSilentForOneTickLessThanAllowedIsOnlySilent()
    {
        SystemReach reach = Only(ServiceReach.Assess(
            [TuneSystem.IsdbSBs],
            [Abandoned(101, Now - Day + TimeSpan.FromTicks(1))],
            Day,
            Now));

        Assert.Equal(ServiceReachLevel.Silent, reach.Level);
        Assert.Equal(0, reach.Services);
    }

    [Fact]
    public void TheMostRecentSightingIsTheOneTheSilenceIsMeasuredFrom()
    {
        SystemReach reach = Only(ServiceReach.Assess(
            [TuneSystem.IsdbSBs],
            [Abandoned(101, Now - TimeSpan.FromDays(9)), Abandoned(102, Now - TimeSpan.FromHours(1))],
            Day,
            Now));

        Assert.Equal(ServiceReachLevel.Silent, reach.Level);
        Assert.Equal(Now - TimeSpan.FromHours(1), reach.LastSeenAt);
    }

    [Fact]
    public void ASystemNoTunerServesIsNotAssessedAtAll()
    {
        IReadOnlyList<SystemReach> assessed = ServiceReach.Assess(
            [TuneSystem.IsdbT],
            [Abandoned(101, Now - TimeSpan.FromDays(9), 27, TuneSystem.IsdbSBs)],
            Day,
            Now);

        Assert.Equal(TuneSystem.IsdbT, Only(assessed).System);
        Assert.Equal(ServiceReachLevel.Unmeasured, Only(assessed).Level);
    }

    [Fact]
    public void EverySystemAskedAboutIsAnsweredOnceAndInOrder()
    {
        IReadOnlyList<SystemReach> assessed = ServiceReach.Assess(
            [TuneSystem.IsdbSCs110, TuneSystem.IsdbT, TuneSystem.IsdbSCs110],
            [],
            Day,
            Now);

        Assert.Equal([TuneSystem.IsdbT, TuneSystem.IsdbSCs110], assessed.Select(reach => reach.System));
    }

    [Fact]
    public void ASilenceOfNoTimeAtAllIsRefused()
    {
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => ServiceReach.Assess([TuneSystem.IsdbT], [], TimeSpan.Zero, Now));

        Assert.Equal("silence", thrown.ParamName);
    }

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
