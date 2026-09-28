using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Reservations;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Reservations;

public sealed class BroadcastGroupServiceTests
{
    private const int Network = 32736;

    private static readonly DateTime Now = ReservationFixtures.Now;

    private static readonly DateTime Opens = Now.AddDays(2);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task BrRd010AReservationOnASuppressedListingIsMovedOntoThePrimaryKeepingWhatItWasGiven()
    {
        Held held = Standing();
        held.List(1024, 101, 0, 120, related: [Link(1025, 201, RelationKind.Moved)]);
        held.List(1025, 201, 0, 120, running: ProgrammeRunning.Running);
        RuleId rule = RuleId.New();
        Reservation booked = held.Book(1024, 101, 0, 120, priority: new Priority(30), rule: rule);

        BroadcastGroupRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Equal([booked.Id], run.Retargeted);
        Assert.Equal(new ProgrammeId(new NetworkId(Network), new ServiceId(1025), new EventId(201)), booked.Programme.Id);
        Assert.Equal(new Priority(30), booked.Priority);
        Assert.Equal(rule, booked.RuleId);
        Assert.Equal(BroadcastGroupRole.MovementPrimary, booked.BroadcastGroupRole);
        Assert.Equal(new BroadcastGroupKey($"movement:{Network}-1024-101"), booked.BroadcastGroupKey);
        Assert.Contains(booked.EpgDivergences, divergence => divergence.Field is DivergedField.Service);
        Assert.Equal(
            ReservationOutcomeKind.ProgrammeMoved,
            Assert.Single(held.Outcomes.Held, outcome => outcome.ReservationId.Equals(booked.Id)).Kind);
        Assert.Contains(AppEventName.Reservations, held.Events.Signalled);
    }

    [Fact]
    public async Task BrRd010WhenThePrimaryChangesTheReservationFollowsItRatherThanBeingMadeAgain()
    {
        Held held = Standing();
        held.List(1024, 101, 0, 120, related: [Link(1025, 201, RelationKind.Moved)]);
        held.List(1025, 201, 30, 120);
        Reservation booked = held.Book(1024, 101, 0, 120);

        Assert.Empty((await held.Service.ReconcileAsync(Cancel)).Retargeted);

        held.List(1025, 201, 30, 120, running: ProgrammeRunning.Running);
        BroadcastGroupRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Equal([booked.Id], run.Retargeted);
        Assert.Equal(1025, booked.ServiceId.Value);
        Assert.Single(held.Reservations.Held);
    }

    [Fact]
    public async Task BrRd010AReservationOnASuppressedListingStandsAsideWhenThePrimaryIsAlreadyReserved()
    {
        Held held = Standing();
        held.List(1024, 101, 0, 120, related: [Link(1025, 201, RelationKind.Moved)]);
        held.List(1025, 201, 0, 120, running: ProgrammeRunning.Running);
        Reservation suppressed = held.Book(1024, 101, 0, 120);
        Reservation primary = held.Book(1025, 201, 0, 120);

        BroadcastGroupRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Equal([suppressed.Id], run.SteppedAside);
        Assert.Equal(ReservationState.Cancelled, suppressed.State);
        Assert.Equal(ReservationCancellation.SameBroadcast, suppressed.Cancellation);
        Assert.Equal(ReservationState.Scheduled, primary.State);
        Assert.Equal(BroadcastGroupRole.MovementPrimary, primary.BroadcastGroupRole);
    }

    [Fact]
    public async Task BrRd010AReservationOnASuppressedListingStaysWhenThePrimaryWasCancelled()
    {
        Held held = Standing();
        held.List(1024, 101, 0, 120, related: [Link(1025, 201, RelationKind.Moved)]);
        held.List(1025, 201, 0, 120, running: ProgrammeRunning.Running);
        Reservation suppressed = held.Book(1024, 101, 0, 120);
        held.Reservations.Standing(ReservationFixtures.Rehydrated(
            ReservationState.Cancelled,
            programme: Reference(1025, 201, 0),
            startAt: Opens,
            endAt: Opens.AddHours(2)));

        BroadcastGroupRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Empty(run.SteppedAside);
        Assert.Empty(run.Retargeted);
        Assert.Equal(ReservationState.Scheduled, suppressed.State);
        Assert.Equal(BroadcastGroupRole.MovementSuppressed, suppressed.BroadcastGroupRole);
    }

    [Fact]
    public async Task BrRd010AReservationWhoseListingLeftTheGuideIsMovedOntoTheListingThatRemains()
    {
        Held held = Standing();
        held.List(1025, 201, 0, 120, related: [Link(1024, 101, RelationKind.Moved)]);
        Reservation booked = held.Book(1024, 101, 0, 120);

        BroadcastGroupRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Equal([booked.Id], run.Retargeted);
        Assert.Equal(1025, booked.ServiceId.Value);
    }

    [Fact]
    public async Task BrRd010TheSegmentsOfARelayAreReservedAfterTheExampleOfTheOneAlreadyReserved()
    {
        Held held = Standing();
        held.List(1024, 101, 0, 60, related: [Link(1026, 301, RelationKind.Relayed)]);
        held.List(1026, 301, 60, 120);
        RuleId rule = RuleId.New();
        Reservation first = held.Book(1024, 101, 0, 60, priority: new Priority(20), rule: rule);

        BroadcastGroupRun run = await held.Service.ReconcileAsync(Cancel);

        Reservation second = Assert.Single(run.Made);
        BroadcastGroupKey key = new($"relay:{Network}-1024-101");
        Assert.Equal(1026, second.ServiceId.Value);
        Assert.Equal(301, second.EventId.Value);
        Assert.Equal(new Priority(20), second.Priority);
        Assert.Equal(rule, second.RuleId);
        Assert.Equal(key, second.BroadcastGroupKey);
        Assert.Equal(BroadcastGroupRole.RelaySegment, second.BroadcastGroupRole);
        Assert.Equal(key, first.BroadcastGroupKey);
        Assert.Equal(BroadcastGroupRole.RelaySegment, first.BroadcastGroupRole);
        Assert.Equal([first.Id], run.Regrouped);
    }

    [Fact]
    public async Task BrRd010ASegmentSomebodyCancelledIsNotReservedAgain()
    {
        Held held = Standing();
        held.List(1024, 101, 0, 60, related: [Link(1026, 301, RelationKind.Relayed)]);
        held.List(1026, 301, 60, 120);
        held.Book(1024, 101, 0, 60);
        held.Reservations.Standing(ReservationFixtures.Rehydrated(
            ReservationState.Cancelled,
            programme: Reference(1026, 301, 60),
            startAt: Opens.AddMinutes(60),
            endAt: Opens.AddMinutes(120)));

        BroadcastGroupRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Empty(run.Made);
        Assert.Equal(2, held.Reservations.Held.Count);
    }

    [Fact]
    public async Task BrRd010ASegmentBeingRecordedStillHasTheSegmentsAfterItReservedButIsNotItselfTouched()
    {
        Held held = Standing(at: Opens.AddMinutes(10));
        held.List(1024, 101, 0, 60, related: [Link(1026, 301, RelationKind.Relayed)]);
        held.List(1026, 301, 60, 120);
        Reservation recording = held.Book(1024, 101, 0, 60, startedAt: Opens);

        BroadcastGroupRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Equal(301, Assert.Single(run.Made).EventId.Value);
        Assert.Equal(BroadcastGroupRole.Standalone, recording.BroadcastGroupRole);
        Assert.Empty(run.Regrouped);
    }

    [Fact]
    public async Task BrRd010AReservationWhoseGroupWentAwayStandsAloneAgain()
    {
        Held held = Standing();
        held.List(1024, 101, 0, 60);
        Reservation booked = held.Book(1024, 101, 0, 60);
        booked.Regroup(new BroadcastGroupKey($"relay:{Network}-1024-101"), BroadcastGroupRole.RelaySegment);

        BroadcastGroupRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Equal([booked.Id], run.Regrouped);
        Assert.Null(booked.BroadcastGroupKey);
        Assert.Equal(BroadcastGroupRole.Standalone, booked.BroadcastGroupRole);
    }

    [Fact]
    public async Task BrRd010AReservationInNoGroupIsLeftAsItWas()
    {
        Held held = Standing();
        held.List(1024, 101, 0, 60);
        held.Book(1024, 101, 0, 60);

        BroadcastGroupRun run = await held.Service.ReconcileAsync(Cancel);

        Assert.Empty(run.Retargeted);
        Assert.Empty(run.Regrouped);
        Assert.Empty(run.SteppedAside);
        Assert.Empty(run.Made);
        Assert.Empty(held.Events.Signalled);
    }

    private static RelatedProgramme Link(int service, int carried, RelationKind kind)
        => new(Network, service, carried, kind);

    private static ProgrammeRef Reference(int service, int carried, int startMinutes)
        => new(new NetworkId(Network), new ServiceId(service), new EventId(carried), Opens.AddMinutes(startMinutes));

    private static Held Standing(DateTime? at = null)
    {
        WatchedWrite write = new();
        HeldOutcomes outcomes = new(write);
        HeldReservations ledger = new(write, outcomes);
        HeldProgrammes programmes = new();
        SilentEvents events = new();
        FixedClock clock = new(at ?? Now);
        TuningByService directory = new()
        {
            Otherwise = TuningResolution.Tunable(
                new CandidateChannelId(Guid.NewGuid()),
                TuningParameters.Terrestrial(27),
                impaired: false),
        };

        ReservationSchedulingService scheduling = new(
            ledger,
            new NoRecordings(),
            new HeldSeating(new TunerCapacity(
                [
                    new TunerSeat("seat0", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                    new TunerSeat("seat1", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                ],
                [])),
            directory,
            write,
            RollingHorizon.Default,
            new SilentEvents(),
            clock);

        return new Held(
            new BroadcastGroupService(ledger, outcomes, programmes, scheduling, write, events, clock),
            ledger,
            outcomes,
            programmes,
            events);
    }

    private sealed record Held(
        BroadcastGroupService Service,
        HeldReservations Reservations,
        HeldOutcomes Outcomes,
        HeldProgrammes Programmes,
        SilentEvents Events)
    {
        public void List(
            int service,
            int carried,
            int startMinutes,
            int endMinutes,
            ProgrammeRunning running = ProgrammeRunning.Undetermined,
            IReadOnlyList<RelatedProgramme>? related = null)
        {
            ProgrammeId id = new(new NetworkId(Network), new ServiceId(service), new EventId(carried));

            Programmes.Programmes.RemoveAll(programme => programme.Id.Equals(id));
            Programmes.Programmes.Add(Programme.Rehydrate(
                id,
                new TransportStreamId(Network),
                Opens.AddMinutes(startMinutes),
                Opens.AddMinutes(endMinutes),
                $"Listing {service}-{carried}",
                "What it is about",
                false,
                Now,
                related: related,
                running: running));
        }

        public Reservation Book(
            int service,
            int carried,
            int startMinutes,
            int endMinutes,
            Priority? priority = null,
            RuleId? rule = null,
            DateTime? startedAt = null)
        {
            Reservation booked = Reservation.Rehydrate(
                ReservationId.New(),
                Reference(service, carried, startMinutes),
                rule,
                priority ?? Priority.Default,
                Opens.AddMinutes(startMinutes),
                Opens.AddMinutes(endMinutes),
                true,
                Margin.None,
                Margin.None,
                ReservationFixtures.Snapshot($"Listing {service}-{carried}"),
                null,
                BroadcastGroupRole.Standalone,
                ReservationState.Scheduled,
                startedAt,
                null,
                false,
                [],
                false,
                null,
                false,
                null,
                Now);

            Reservations.Standing(booked);

            return booked;
        }
    }
}
