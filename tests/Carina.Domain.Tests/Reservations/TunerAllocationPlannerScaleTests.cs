using System.Diagnostics;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

using Xunit.Abstractions;

namespace Carina.Domain.Tests.Reservations;

[CollectionDefinition(nameof(TunerAllocationPlannerScale), DisableParallelization = true)]
public sealed class TunerAllocationPlannerScale;

[Trait("Category", "Scale")]
[Collection(nameof(TunerAllocationPlannerScale))]
public sealed class TunerAllocationPlannerScaleTests(ITestOutputHelper output)
{
    private const int EightDays = 8;

    private const int Reservations = 1000;

    private const int RecordingsUnderWay = 4;

    private const int MeasuredRuns = 5;

    private const int FewestContended = 100;

    private const int FewestNamed = 100;

    private const string WhyTheProcessorClockIsTheGate =
        "The whole suite runs nine test hosts at once, so a wall clock reading says as much about how busy the "
        + "machine was as about the planning: the same run has been measured at 315 ms and at 2714 ms. What the "
        + "budget is about is the work, so the gate is the processor time the work itself spent, which does not "
        + "move with the load. Three things hold the reading steady: the collection is barred from running "
        + "beside the rest of this assembly, so nothing else in this process is counted against it; a warm run "
        + "is thrown away first; and the quickest of several runs is taken. The wall clock is printed beside it "
        + "rather than asserted, because it is worth seeing and cannot hold on its own.";

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);

    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<TuningParameters> Channels =
    [
        .. Enumerable.Range(0, 10).Select(slot => TuningParameters.Terrestrial(13 + (slot * 2))),
        .. new[] { 1, 3, 5, 9 }.Select((slot, order) => TuningParameters.Bs(slot, new TransportStreamId(16625 + order))),
        .. Enumerable.Range(0, 2).Select(slot => TuningParameters.Cs110(2 + (slot * 2))),
    ];

    [Fact]
    public void EightDaysOfReservationsArePlannedInsideTheBudget()
    {
        AllocationCandidate[] candidates = [.. Enumerable.Range(0, Reservations).Select(Reservation)];
        TunerCapacity capacity = Capacity();

        TunerAllocationPlanner.Plan(candidates, capacity, RollingHorizon.Default, Now);

        List<TimeSpan> onTheWallClock = [];
        List<TimeSpan> onTheProcessorClock = [];
        AllocationPlan plan = null!;

        for (int run = 0; run < MeasuredRuns; run++)
        {
            TimeSpan spentBefore = Process.GetCurrentProcess().TotalProcessorTime;
            long started = Stopwatch.GetTimestamp();
            plan = TunerAllocationPlanner.Plan(candidates, capacity, RollingHorizon.Default, Now);
            onTheWallClock.Add(Stopwatch.GetElapsedTime(started));
            onTheProcessorClock.Add(Process.GetCurrentProcess().TotalProcessorTime - spentBefore);
        }

        TimeSpan quickest = onTheProcessorClock.Min();

        output.WriteLine($"{Reservations} reservations over {EightDays} days across {Channels.Count} channels "
            + $"on {capacity.SeatCount} seats: {plan.Contended.Count} contended, "
            + $"{plan.Contended.Sum(decision => decision.Instead.Count)} named as recorded instead.");
        output.WriteLine("processor: " + string.Join(
            ", ",
            onTheProcessorClock.Select(took => $"{took.TotalMilliseconds:F1} ms")));
        output.WriteLine("wall: " + string.Join(
            ", ",
            onTheWallClock.Select(took => $"{took.TotalMilliseconds:F1} ms")));
        output.WriteLine(WhyTheProcessorClockIsTheGate);

        Assert.Equal(Reservations, plan.Decisions.Count);

        Assert.True(
            plan.Contended.Count > FewestContended,
            $"only {plan.Contended.Count} of {Reservations} reservations were contended, so the contended path "
            + $"and everything it names was barely measured; wanted more than {FewestContended}.");

        int named = plan.Contended.Sum(decision => decision.Instead.Count);

        Assert.True(
            named > FewestNamed,
            $"the {plan.Contended.Count} contended reservations between them named {named} recordings as "
            + $"recorded instead, so the check below walked almost nothing; wanted more than {FewestNamed}.");

        NothingFromAnotherPoolWasNamed(candidates, plan, capacity);

        Assert.True(
            quickest > TimeSpan.Zero,
            $"the processor clock read {quickest.TotalMilliseconds:F1} ms for {Reservations} reservations, "
            + "which is no reading at all rather than a fast one.");

        Assert.True(
            quickest < Budget,
            $"the quickest of {MeasuredRuns} runs spent {quickest.TotalMilliseconds:F1} ms of processor time "
            + $"planning {Reservations} reservations, over the {Budget.TotalMilliseconds:F0} ms budget.");
    }

    private static void NothingFromAnotherPoolWasNamed(
        IReadOnlyList<AllocationCandidate> candidates,
        AllocationPlan plan,
        TunerCapacity capacity)
    {
        Dictionary<ReservationId, AllocationCandidate> byId = candidates.ToDictionary(candidate => candidate.Id);

        foreach (AllocationDecision decision in plan.Contended)
        {
            TuneSystem lost = byId[decision.Id].Tuning!.System;

            foreach (ReservationId named in decision.Instead)
            {
                TuneSystem took = byId[named].Tuning!.System;

                Assert.True(
                    capacity.SharesSeats(took, lost),
                    $"a {took} recording was named as recorded instead of a {lost} reservation, and no seat "
                    + "serves both, so it took nothing the reservation could have had.");
            }
        }
    }

    private static TunerCapacity Capacity()
        => new(
            [
                .. Enumerable.Range(0, 3).Select(seat =>
                    new TunerSeat($"seat{seat}", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)),
                .. Enumerable.Range(3, 2).Select(seat =>
                    new TunerSeat($"seat{seat}", BroadcastReception.Of(TunerKind.Satellite), Faulted: false)),
            ],
            []);

    private static AllocationCandidate Reservation(int order)
    {
        DateTime opens = Now.AddMinutes(order * EightDays * 24 * 60 / Reservations);

        return new AllocationCandidate(
            new ReservationId(new Guid(order + 1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0])),
            new ProgrammeRef(
                new NetworkId(32736),
                new ServiceId(1024 + (order % Channels.Count)),
                new EventId(4000 + order),
                opens),
            new Priority(1 + (order % 99)),
            Channels[order % Channels.Count],
            opens,
            opens.AddMinutes(60),
            endAtConfirmed: order % 7 is not 0,
            pinned: order < RecordingsUnderWay);
    }
}
