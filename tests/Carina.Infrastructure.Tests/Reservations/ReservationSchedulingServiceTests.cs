using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Reservations;

namespace Carina.Infrastructure.Tests.Reservations;

public sealed class ReservationSchedulingServiceTests
{
    private static readonly DateTime Now = ReservationFixtures.Now;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TuningParameters Terrestrial27 = TuningParameters.Terrestrial(27);

    private static readonly TuningParameters Terrestrial29 = TuningParameters.Terrestrial(29);

    [Fact]
    public async Task ANewReservationIsSettledAndWrittenInOneTurn()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, Terrestrial27);

        Reservation planned = ReservationFixtures.Planned();
        SchedulingRun run = await Scheduler(ledger, directory, write, Seats(TunerKind.Terrestrial))
            .CreateAsync(planned, Cancel);

        Assert.True(run.Settled);
        Assert.Equal(AllocationVerdict.Secured, run.Plan.For(planned.Id).Verdict);
        Assert.Equal(ReservationState.Scheduled, planned.State);
        Assert.Equal(1, write.Committed);
        Assert.Equal(0, write.RolledBack);
        Assert.Contains($"add {planned.Id.Value}", ledger.Wrote);
        Assert.Empty(ledger.WroteOutsideAWrite);
    }

    [Fact]
    public async Task TheOneThatLosesTheOnlySeatIsWrittenAsContended()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, Terrestrial27);
        directory.Answer(1032, Terrestrial29);

        Reservation kept = ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId()),
            priority: new Priority(20));
        Reservation lost = ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), serviceId: 1032),
            priority: new Priority(10));

        ReservationSchedulingService scheduler = Scheduler(ledger, directory, write, Seats(TunerKind.Terrestrial));
        await scheduler.CreateAsync(kept, Cancel);
        SchedulingRun run = await scheduler.CreateAsync(lost, Cancel);

        Assert.True(run.Settled);
        Assert.Equal(ReservationState.Scheduled, kept.State);
        Assert.Equal(ReservationState.Conflict, lost.State);
        Assert.Equal([kept.Id], run.Plan.For(lost.Id).Instead);
    }

    [Fact]
    public async Task NothingIsWrittenWhenTheTunersCannotBeCounted()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, Terrestrial27);

        Reservation planned = ReservationFixtures.Planned();
        SchedulingRun run = await Scheduler(ledger, directory, write, capacity: null).CreateAsync(planned, Cancel);

        Assert.False(run.Settled);
        Assert.Equal(SchedulingRefusal.CapacityUnknown, run.Refusal);
        Assert.Throws<InvalidOperationException>(() => run.Plan);
        Assert.Equal(0, write.Opened);
        Assert.Empty(ledger.Wrote);
        Assert.Empty(ledger.Held);
    }

    [Fact]
    public async Task NothingIsWrittenWhenTheSelectionCannotBeAnsweredEither()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, TuningResolution.Refused(TuningRefusal.LedgerUnreadable));

        Reservation planned = ReservationFixtures.Planned();
        SchedulingRun run = await Scheduler(ledger, directory, write, Seats(TunerKind.Terrestrial))
            .CreateAsync(planned, Cancel);

        Assert.False(run.Settled);
        Assert.Equal(SchedulingRefusal.CapacityUnknown, run.Refusal);
        Assert.Empty(ledger.Wrote);
        Assert.Empty(ledger.Held);
        Assert.Equal(ReservationState.Scheduled, planned.State);
        Assert.False(planned.ReceptionUnavailable);
    }

    [Fact]
    public async Task ASeatThatCannotBePlacedIsLeftOutRatherThanStoppingTheRun()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, Terrestrial27);
        directory.Answer(1032, TuningResolution.Refused(TuningRefusal.CapacityUnknown));

        Reservation reachable = ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId()));
        Reservation beyond = ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), serviceId: 1032));
        ledger.Standing(beyond);

        SchedulingRun run = await Scheduler(ledger, directory, write, Unplaceable("adapter9"))
            .CreateAsync(reachable, Cancel);

        Assert.True(run.Settled);
        Assert.Equal(1, run.SeatsLeftOut);
        Assert.Equal(AllocationVerdict.Secured, run.Plan.For(reachable.Id).Verdict);
        Assert.Equal(AllocationVerdict.Unreachable, run.Plan.For(beyond.Id).Verdict);
        Assert.True(beyond.ReceptionUnavailable);
        Assert.Equal(ReservationState.Scheduled, reachable.State);
        Assert.Contains($"add {reachable.Id.Value}", ledger.Wrote);
    }

    [Fact]
    public async Task ARunThatPlacedEverySeatSaysItLeftNoneOut()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, Terrestrial27);

        SchedulingRun run = await Scheduler(ledger, directory, write, Seats(TunerKind.Terrestrial))
            .CreateAsync(ReservationFixtures.Planned(), Cancel);

        Assert.Equal(0, run.SeatsLeftOut);
    }

    [Fact]
    public async Task NowhereToTuneIsMarkedRatherThanTakenForSecured()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, TuningResolution.Refused(TuningRefusal.NoSelectedChannel));

        Reservation planned = ReservationFixtures.Planned();
        SchedulingRun run = await Scheduler(ledger, directory, write, Seats(TunerKind.Terrestrial))
            .CreateAsync(planned, Cancel);

        Assert.Equal(AllocationVerdict.Unreachable, run.Plan.For(planned.Id).Verdict);
        Assert.True(planned.ReceptionUnavailable);
        Assert.Equal(Now, planned.ReceptionUnavailableSince);
        Assert.Equal(ReservationState.Scheduled, planned.State);
    }

    [Fact]
    public async Task AServiceThatHasSomewhereToTuneAgainStopsBeingMarked()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, TuningResolution.Refused(TuningRefusal.NoSuchService));

        Reservation planned = ReservationFixtures.Planned();
        ReservationSchedulingService scheduler = Scheduler(ledger, directory, write, Seats(TunerKind.Terrestrial));
        await scheduler.CreateAsync(planned, Cancel);

        Assert.True(planned.ReceptionUnavailable);

        directory.Answer(1024, Terrestrial27);
        SchedulingRun again = await scheduler.RecalculateAsync(Cancel);

        Assert.Equal(AllocationVerdict.Secured, again.Plan.For(planned.Id).Verdict);
        Assert.False(planned.ReceptionUnavailable);
        Assert.Null(planned.ReceptionUnavailableSince);
    }

    [Fact]
    public async Task TheGroupingKeyIsResolvedAgainOnEveryRunRatherThanKept()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, Terrestrial27);
        directory.Answer(1032, Terrestrial27);

        Reservation first = ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), serviceId: 1024),
            priority: new Priority(20));
        Reservation second = ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), serviceId: 1032),
            priority: new Priority(10));

        ReservationSchedulingService scheduler = Scheduler(ledger, directory, write, Seats(TunerKind.Terrestrial));
        await scheduler.CreateAsync(first, Cancel);
        await scheduler.CreateAsync(second, Cancel);

        Assert.Equal(ReservationState.Scheduled, second.State);

        directory.Answer(1032, Terrestrial29);
        SchedulingRun again = await scheduler.RecalculateAsync(Cancel);

        Assert.Equal(AllocationVerdict.Secured, again.Plan.For(first.Id).Verdict);
        Assert.Equal(AllocationVerdict.Contended, again.Plan.For(second.Id).Verdict);
        Assert.Equal(ReservationState.Conflict, second.State);
    }

    [Fact]
    public async Task OneRunAsksTheSelectionOnceForEachServiceAndTheNextRunAsksAgain()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, Terrestrial27);

        ledger.Standing(
            ReservationFixtures.Planned(programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId())),
            ReservationFixtures.Planned(programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId())),
            ReservationFixtures.Planned(programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId())));

        ReservationSchedulingService scheduler = Scheduler(ledger, directory, write, Seats(TunerKind.Terrestrial));
        await scheduler.RecalculateAsync(Cancel);

        Assert.Equal([1024], directory.Asked);

        await scheduler.RecalculateAsync(Cancel);

        Assert.Equal([1024, 1024], directory.Asked);
    }

    [Fact]
    public async Task APreviewWeighsTheProposedAgainstWhatIsStandingWithoutTouchingIt()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, Terrestrial27);
        directory.Answer(1032, Terrestrial29);

        Reservation standing = ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId()),
            priority: new Priority(10));
        ReservationSchedulingService scheduler = Scheduler(ledger, directory, write, Seats(TunerKind.Terrestrial));
        await scheduler.CreateAsync(standing, Cancel);

        Reservation proposed = ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), serviceId: 1032),
            priority: new Priority(20));

        ledger.Wrote.Clear();
        SchedulingRun preview = await scheduler.PreviewAsync([proposed], Cancel);

        Assert.Equal(AllocationVerdict.Secured, preview.Plan.For(proposed.Id).Verdict);
        Assert.Equal(AllocationVerdict.Contended, preview.Plan.For(standing.Id).Verdict);
        Assert.Equal(ReservationState.Scheduled, standing.State);
        Assert.Empty(ledger.Wrote);
        Assert.DoesNotContain(proposed, ledger.Held);
    }

    [Fact]
    public async Task APreviewOfNothingStillWeighsWhatIsStanding()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, Terrestrial27);

        Reservation standing = ReservationFixtures.Planned();
        ledger.Standing(standing);

        SchedulingRun preview = await Scheduler(ledger, directory, write, Seats(TunerKind.Terrestrial))
            .PreviewAsync([], Cancel);

        Assert.Equal(AllocationVerdict.Secured, preview.Plan.For(standing.Id).Verdict);
    }

    [Fact]
    public async Task APreviewSaysNothingWhenTheTunersCannotBeCounted()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, Terrestrial27);
        ledger.Standing(ReservationFixtures.Planned());

        SchedulingRun preview = await Scheduler(ledger, directory, write, capacity: null)
            .PreviewAsync([ReservationFixtures.Planned()], Cancel);

        Assert.False(preview.Settled);
        Assert.Equal(SchedulingRefusal.CapacityUnknown, preview.Refusal);
        Assert.Empty(directory.Asked);
    }

    [Fact]
    public async Task AReservationWhoseAfterMarginIsStillRunningKeepsItsSeat()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, Terrestrial27);
        directory.Answer(1032, Terrestrial29);

        ledger.Standing(ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId()),
            priority: new Priority(20),
            startAt: Now.AddMinutes(-65),
            endAt: Now.AddMinutes(-5),
            marginAfter: Margin.OfSeconds(1800)));

        Reservation wanting = ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), serviceId: 1032),
            priority: new Priority(10),
            startAt: Now,
            endAt: Now.AddMinutes(20));

        SchedulingRun run = await Scheduler(ledger, directory, write, Seats(TunerKind.Terrestrial))
            .CreateAsync(wanting, Cancel);

        Assert.Equal(AllocationVerdict.Contended, run.Plan.For(wanting.Id).Verdict);
    }

    [Fact]
    public async Task AReservationFurtherOutThanTheGuideReachesIsWeighedToo()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, Terrestrial27);
        directory.Answer(1032, Terrestrial29);

        ledger.Standing(ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId()),
            priority: new Priority(20),
            startAt: Now.AddDays(30),
            endAt: Now.AddDays(30).AddHours(1)));

        Reservation wanting = ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), serviceId: 1032),
            priority: new Priority(10),
            startAt: Now.AddDays(30),
            endAt: Now.AddDays(30).AddHours(1));

        SchedulingRun run = await Scheduler(ledger, directory, write, Seats(TunerKind.Terrestrial))
            .CreateAsync(wanting, Cancel);

        Assert.Equal(AllocationVerdict.Contended, run.Plan.For(wanting.Id).Verdict);
    }

    [Fact]
    public async Task AWriteThatFailsHalfWayIsRolledBack()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write) { RefuseToAdd = new InvalidOperationException("the ledger said no") };
        TuningByService directory = new();
        directory.Answer(1024, Terrestrial27);

        Reservation planned = ReservationFixtures.Planned();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Scheduler(ledger, directory, write, Seats(TunerKind.Terrestrial)).CreateAsync(planned, Cancel));

        Assert.Equal(1, write.RolledBack);
        Assert.Equal(0, write.Committed);
        Assert.Empty(ledger.Held);
    }

    [Fact]
    public async Task RecalculatingAnEmptyLedgerIsAPlanThatSaysNothing()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);

        SchedulingRun run = await Scheduler(ledger, new TuningByService(), write, Seats(TunerKind.Terrestrial))
            .RecalculateAsync(Cancel);

        Assert.True(run.Settled);
        Assert.Empty(run.Plan.Decisions);
    }

    [Fact]
    public async Task ARecordingStillRunningKeepsItsSeatAndTheNextOneWaits()
    {
        WatchedWrite write = new();
        HeldReservations ledger = new(write);
        TuningByService directory = new();
        directory.Answer(1024, Terrestrial27);
        directory.Answer(1032, Terrestrial29);

        Reservation running = ReservationFixtures.Rehydrated(
            ReservationState.Scheduled,
            startedAt: Now.AddMinutes(-10),
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId()),
            startAt: Now.AddMinutes(-10),
            endAt: Now.AddMinutes(50));
        ledger.Standing(running);

        Reservation wanting = ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), serviceId: 1032),
            startAt: Now,
            endAt: Now.AddMinutes(30),
            priority: new Priority(99));

        SchedulingRun run = await Scheduler(ledger, directory, write, Seats(TunerKind.Terrestrial))
            .CreateAsync(wanting, Cancel);

        Assert.Equal(AllocationVerdict.Pinned, run.Plan.For(running.Id).Verdict);
        Assert.Equal(AllocationVerdict.Contended, run.Plan.For(wanting.Id).Verdict);
        Assert.Equal(ReservationState.Scheduled, running.State);
        Assert.Equal(ReservationState.Conflict, wanting.State);
    }

    private static TunerCapacity Unplaceable(params string[] deviceIds)
        => new(
            [new TunerSeat("seat0", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)],
            deviceIds);

    private static TunerCapacity Seats(params TunerKind[] kinds)
        => new(
            [
                .. kinds.Select((kind, index) =>
                    new TunerSeat($"seat{index}", BroadcastReception.Of(kind), Faulted: false)),
            ],
            []);

    private static ReservationSchedulingService Scheduler(
        HeldReservations ledger,
        TuningByService directory,
        WatchedWrite write,
        TunerCapacity? capacity)
        => new(
            ledger,
            new HeldSeating(capacity),
            directory,
            write,
            RollingHorizon.Default,
            new FixedClock(Now));
}
