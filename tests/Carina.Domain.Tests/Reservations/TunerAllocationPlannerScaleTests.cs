using System.Diagnostics;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

[Trait("Category", "Scale")]
public sealed class TunerAllocationPlannerScaleTests
{
    private const int EightDays = 8;

    private const int Reservations = 1000;

    private const int RecordingsUnderWay = 4;

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
        TunerCapacity capacity = new(
            [
                .. Enumerable.Range(0, 3).Select(seat =>
                    new TunerSeat($"seat{seat}", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)),
                .. Enumerable.Range(3, 2).Select(seat =>
                    new TunerSeat($"seat{seat}", BroadcastReception.Of(TunerKind.Satellite), Faulted: false)),
            ],
            []);

        Stopwatch clock = Stopwatch.StartNew();
        AllocationPlan plan = TunerAllocationPlanner.Plan(candidates, capacity, RollingHorizon.Default, Now);
        clock.Stop();

        Assert.Equal(Reservations, plan.Decisions.Count);
        Assert.NotEmpty(plan.Contended);
        Assert.NotEmpty(plan.Contended[0].Instead);
        Assert.True(
            clock.Elapsed < Budget,
            $"planning {Reservations} reservations over {EightDays} days took {clock.ElapsedMilliseconds} ms, "
            + $"and the budget is {Budget.TotalMilliseconds} ms");
    }

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
