using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

/// <summary>
/// The two edges the allocation is decided on that a whole-number example never reaches: a tuner
/// that serves more than one broadcast type, which is one seat however many types it answers for,
/// and the gap between one broadcast and the next, which is the gap the margins ask for rather
/// than the one the schedule shows.
/// </summary>
public sealed class TunerAllocationPlannerBoundaryTests
{
    private static readonly DateTime Now = ReservationFactory.Now;

    private static readonly TuningParameters Terrestrial27 = TuningParameters.Terrestrial(27);

    private static readonly TuningParameters Terrestrial29 = TuningParameters.Terrestrial(29);

    private static readonly TuningParameters BsSlot = TuningParameters.Bs(15, new TransportStreamId(16625));

    [Fact]
    public void OneTunerServingBothKindsIsOneSeatRatherThanOnePerKind()
    {
        AllocationCandidate terrestrial = Candidate(Terrestrial27, priority: 20, eventId: 4001);
        AllocationCandidate satellite = Candidate(BsSlot, priority: 10, eventId: 4002);

        AllocationPlan plan = Planned([terrestrial, satellite], ServingBothKinds());

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, terrestrial));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, satellite));
        Assert.Equal([terrestrial.Id], plan.For(satellite.Id).Instead);
    }

    [Fact]
    public void TheTunerServingBothKindsCarriesEitherOfThemWhereTheyDoNotMeet()
    {
        AllocationCandidate terrestrial = Candidate(Terrestrial27, fromMinutes: 0, eventId: 4001);
        AllocationCandidate satellite = Candidate(BsSlot, fromMinutes: 60, eventId: 4002);

        AllocationPlan plan = Planned([terrestrial, satellite], ServingBothKinds());

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, terrestrial));
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, satellite));
    }

    [Fact]
    public void ASecondSeatIsWhatLetsBothKindsBeRecordedAtOnce()
    {
        AllocationCandidate terrestrial = Candidate(Terrestrial27, priority: 20, eventId: 4001);
        AllocationCandidate satellite = Candidate(BsSlot, priority: 10, eventId: 4002);
        TunerCapacity bothAndOneMore = new(
            [
                new TunerSeat("seat0", [TuneSystem.IsdbT, TuneSystem.IsdbSBs], Faulted: false),
                new TunerSeat("seat1", [TuneSystem.IsdbT], Faulted: false),
            ],
            []);

        AllocationPlan plan = Planned([terrestrial, satellite], bothAndOneMore);

        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, terrestrial));
        Assert.Equal(AllocationVerdict.Secured, Verdict(plan, satellite));
    }

    [Fact]
    public void ARecordingOnTheSeatThatServesBothKindsLeavesNothingForTheOtherKind()
    {
        AllocationCandidate recording = Candidate(BsSlot, priority: 1, pinned: true, eventId: 4001);
        AllocationCandidate wanted = Candidate(Terrestrial27, priority: 99, eventId: 4002);

        AllocationPlan plan = Planned([recording, wanted], ServingBothKinds());

        Assert.Equal(AllocationVerdict.Pinned, Verdict(plan, recording));
        Assert.Equal(AllocationVerdict.Contended, Verdict(plan, wanted));
        Assert.Equal([recording.Id], plan.For(wanted.Id).Instead);
    }

    [Fact]
    public void ASeatServingBothKindsTakesBothOfThemDownWhenItIsFaulted()
    {
        AllocationCandidate terrestrial = Candidate(Terrestrial27, priority: 20, eventId: 4001);
        AllocationCandidate satellite = Candidate(BsSlot, priority: 10, eventId: 4002);
        TunerCapacity withAFaultedSeat = new(
            [
                new TunerSeat("seat0", [TuneSystem.IsdbT, TuneSystem.IsdbSBs], Faulted: true),
                new TunerSeat("seat1", [TuneSystem.IsdbT], Faulted: false),
            ],
            []);

        AllocationPlan counted = Planned([terrestrial, satellite], withAFaultedSeat);
        AllocationPlan healthy = Planned([terrestrial, satellite], withAFaultedSeat.Healthy);

        Assert.Equal(AllocationVerdict.Secured, Verdict(counted, satellite));
        Assert.Equal(AllocationVerdict.Secured, Verdict(healthy, terrestrial));
        Assert.Equal(AllocationVerdict.Contended, Verdict(healthy, satellite));
    }

    [Theory]
    [InlineData(40, AllocationVerdict.Secured)]
    [InlineData(39, AllocationVerdict.Contended)]
    public void TheGapTwoBroadcastsNeedIsTheOneTheirMarginsAskFor(
        int secondsBetweenThem,
        AllocationVerdict expected)
    {
        Reservation ends = ReservationFactory.Planned(
            priority: new Priority(20),
            programme: ReservationFactory.Programme(4001, Now.AddHours(-1)),
            marginAfter: Margin.OfSeconds(30));
        Reservation begins = ReservationFactory.Planned(
            priority: new Priority(10),
            programme: ReservationFactory.Programme(4002, Now.AddSeconds(secondsBetweenThem)),
            marginBefore: Margin.OfSeconds(10));

        AllocationPlan plan = Planned(
            [AllocationCandidate.Of(ends, Terrestrial27), AllocationCandidate.Of(begins, Terrestrial29)],
            Capacity(TunerKind.Terrestrial));

        Assert.Equal(Now, ends.EndAt);
        Assert.Equal(AllocationVerdict.Secured, plan.For(ends.Id).Verdict);
        Assert.Equal(expected, plan.For(begins.Id).Verdict);
    }

    private static AllocationPlan Planned(IReadOnlyList<AllocationCandidate> candidates, TunerCapacity capacity)
        => TunerAllocationPlanner.Plan(candidates, capacity, RollingHorizon.Default, Now);

    private static AllocationVerdict Verdict(AllocationPlan plan, AllocationCandidate candidate)
        => plan.For(candidate.Id).Verdict;

    private static TunerCapacity ServingBothKinds()
        => new([new TunerSeat("seat0", [TuneSystem.IsdbT, TuneSystem.IsdbSBs], Faulted: false)], []);

    private static TunerCapacity Capacity(params TunerKind[] kinds)
        => new(
            [.. kinds.Select((kind, index) => new TunerSeat($"seat{index}", BroadcastReception.Of(kind), Faulted: false))],
            []);

    private static AllocationCandidate Candidate(
        TuningParameters tuning,
        int priority = Priority.DefaultValue,
        int fromMinutes = 0,
        bool pinned = false,
        int eventId = 4001)
    {
        DateTime opens = Now.AddHours(2).AddMinutes(fromMinutes);

        return new AllocationCandidate(
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(eventId), opens),
            new Priority(priority),
            tuning,
            opens,
            opens.AddMinutes(60),
            endAtConfirmed: true,
            pinned);
    }
}
