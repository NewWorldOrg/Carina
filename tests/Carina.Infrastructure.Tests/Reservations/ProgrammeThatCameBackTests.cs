using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Programmes;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Rules;
using Carina.Infrastructure.Tests.Rules;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Reservations;

/// <summary>
/// A broadcaster that drops a programme from the guide and announces it again leaves a cancelled
/// reservation sitting on the one key that names that broadcast. Read end to end: the rule makes
/// the reservation, the guide takes it out when the programme goes, and the rule brings that same
/// row back when the programme is announced again.
/// </summary>
public sealed class ProgrammeThatCameBackTests
{
    private const int Network = 4;

    private const int Carried = 32_736;

    private const int Listed = 1049;

    private const int Taken = 1;

    private const int Beside = 2;

    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ABroadcastTheGuideDroppedAndAnnouncedAgainIsReservedByTheRowItLeftBehind()
    {
        World world = World.Taking("keyword=hill");
        world.Announce(Taken, "hill walking");

        Reservation booked = Assert.Single((await world.Applying.EverythingAsync(Cancel)).Made);

        world.Dropped(Taken);

        Assert.Equal([booked.Id], (await world.Guiding.ReconcileAsync(Cancel)).Cancelled);
        Assert.Equal(ReservationState.Cancelled, booked.State);
        Assert.Equal(ReservationCancellation.ProgrammeGone, booked.Cancellation);

        world.Announce(Taken, "hill walking", heard: Now.AddHours(2));

        RuleApplicationRun back = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal([booked.Id], [.. back.Revived.Select(reservation => reservation.Id)]);
        Assert.Empty(back.Made);
        Assert.Equal(ReservationState.Scheduled, booked.State);
        Assert.Null(booked.Cancellation);
        Assert.False(booked.EpgMissing);
        Assert.Single(world.Reservations.Held);
    }

    [Fact]
    public async Task AReservationBroughtBackIsLeftWhereItIsByTheNextReadingOfTheGuide()
    {
        World world = World.Taking("keyword=hill");
        world.Announce(Taken, "hill walking");

        Reservation booked = Assert.Single((await world.Applying.EverythingAsync(Cancel)).Made);

        world.Dropped(Taken);
        await world.Guiding.ReconcileAsync(Cancel);
        world.Announce(Taken, "hill walking", heard: Now.AddHours(2));
        await world.Applying.EverythingAsync(Cancel);

        GuideRun again = await world.Guiding.ReconcileAsync(Cancel);

        Assert.Empty(again.Cancelled);
        Assert.Empty(again.Followed);
        Assert.Equal(ReservationState.Scheduled, booked.State);
    }

    [Fact]
    public async Task ABroadcastThatWentAndCameBackIsWrittenDownOnceEachWayHoweverOftenItDoesIt()
    {
        World world = World.Taking("keyword=hill");
        world.Announce(Taken, "hill walking");

        Reservation booked = Assert.Single((await world.Applying.EverythingAsync(Cancel)).Made);

        for (int round = 1; round <= 2; round++)
        {
            world.Dropped(Taken);
            await world.Guiding.ReconcileAsync(Cancel);
            world.Announce(Taken, "hill walking", heard: Now.AddHours(1 + round));
            await world.Applying.EverythingAsync(Cancel);
        }

        Assert.Equal(
            [ReservationOutcomeKind.ProgrammeGone, ReservationOutcomeKind.ProgrammeReturned],
            [.. world.Outcomes.Held.Select(outcome => outcome.Kind).Order()]);
        Assert.All(world.Outcomes.Held, outcome => Assert.Equal(booked.Id, outcome.ReservationId));
    }

    [Fact]
    public async Task AReservationSomebodyCancelledIsNotBroughtBackThoughTheRuleStillTakesTheProgramme()
    {
        World world = World.Taking("keyword=hill");
        world.Announce(Taken, "hill walking");

        Reservation booked = Assert.Single((await world.Applying.EverythingAsync(Cancel)).Made);

        await world.Scheduling.ReviseAsync(
            booked,
            new ReservationRevision { Move = ReservationMove.Cancel },
            Cancel);

        Assert.Equal(ReservationCancellation.ByHand, booked.Cancellation);

        RuleApplicationRun again = await world.Applying.EverythingAsync(Cancel);

        Assert.Empty(again.Revived);
        Assert.Empty(again.Made);
        Assert.Equal(ReservationState.Cancelled, booked.State);
        Assert.Empty(world.Outcomes.Held);
    }

    [Fact]
    public async Task AReservationNoRuleMadeIsNotBroughtBackByARuleThatTakesTheProgramme()
    {
        World world = World.Taking("keyword=hill");
        world.Announce(Taken, "hill walking");
        Reservation booked = world.BookedByHand(Taken);

        world.Dropped(Taken);
        await world.Guiding.ReconcileAsync(Cancel);
        world.Announce(Taken, "hill walking", heard: Now.AddHours(2));

        RuleApplicationRun again = await world.Applying.EverythingAsync(Cancel);

        Assert.Empty(again.Revived);
        Assert.Empty(again.Made);
        Assert.Equal(ReservationState.Cancelled, booked.State);
    }

    [Fact]
    public async Task AReservationWhoseWindowHasAlreadyClosedIsNotBroughtBackByTheProgrammeReturning()
    {
        World world = World.Taking("keyword=hill");
        world.Announce(Taken, "hill walking");

        Reservation closed = world.StandingCancelled(
            Taken,
            startAt: Now.AddHours(-2),
            endAt: Now.AddHours(-1));

        RuleApplicationRun again = await world.Applying.EverythingAsync(Cancel);

        Assert.Empty(again.Revived);
        Assert.Empty(again.Made);
        Assert.Equal(ReservationState.Cancelled, closed.State);
        Assert.Empty(world.Outcomes.Held);
    }

    private static Rule Written(string query)
        => Rule.Draft(
            new RuleId(new Guid("00000001-0000-0000-0000-000000000000")),
            "a rule",
            new RuleQuery(query),
            Priority.Default,
            true,
            Margin.None,
            Margin.None,
            Now.AddDays(-30));

    private sealed class World
    {
        private long revisions;

        private World(string query)
        {
            Write = new WatchedWrite();
            Outcomes = new HeldOutcomes(Write);
            Reservations = new HeldReservations(Write, Outcomes);
            Streams = new CountedStreams(
            [
                new BroadcastStream(
                    new NetworkId(Network),
                    new TransportStreamId(Carried),
                    TuningParameters.Terrestrial(27),
                    [new ServiceId(Listed)]),
            ]);
            Rules.Rules.Add(Written(query));

            var clock = new FixedClock(Now);
            var tuning = new TuningByService
            {
                Otherwise = TuningResolution.Tunable(
                    new CandidateChannelId(Guid.NewGuid()),
                    TuningParameters.Terrestrial(27),
                    impaired: false),
            };

            Scheduling = new ReservationSchedulingService(
                Reservations,
                new HeldSeating(new TunerCapacity(
                    [new TunerSeat("seat0", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)],
                    [])),
                tuning,
                Write,
                RollingHorizon.Default,
                new SilentEvents(),
                clock);

            Applying = new RuleApplicationService(
                Rules,
                Programmes,
                Reservations,
                Outcomes,
                new HeldStreamVisits(),
                Streams,
                Scheduling,
                new RuleMatcher(new ProgrammeSearchScope(Streams, new CountedServices()), clock),
                new RuleApplicationSettings(),
                Write,
                new SilentEvents(),
                clock);

            Guiding = new ReservationGuideService(
                Reservations,
                Outcomes,
                Programmes,
                Scheduling,
                Write,
                new SilentEvents(),
                clock);
        }

        public HeldRules Rules { get; } = new();

        public HeldProgrammes Programmes { get; } = new();

        public HeldReservations Reservations { get; }

        public HeldOutcomes Outcomes { get; }

        public CountedStreams Streams { get; }

        public WatchedWrite Write { get; }

        public ReservationSchedulingService Scheduling { get; }

        public RuleApplicationService Applying { get; }

        public ReservationGuideService Guiding { get; }

        public static World Taking(string query) => new(query);

        public void Announce(int carried, string name, DateTime? heard = null)
        {
            Programmes.Programmes.RemoveAll(programme => programme.EventId.Value == carried);

            Programme announced = Programme.Rehydrate(
                new ProgrammeId(new NetworkId(Network), new ServiceId(Listed), new EventId(carried)),
                new TransportStreamId(Carried),
                Now.AddHours(2),
                Now.AddHours(3),
                name,
                "a summary",
                false,
                Now,
                lastHeardAt: heard ?? Now);

            announced.MarkRevision(++revisions);
            Programmes.Programmes.Add(announced);
        }

        /// <summary>
        /// What a whole reading of the service looks like from the outside once the broadcaster has
        /// stopped announcing this programme: the programme is not there, and what is still
        /// announced beside it comes away marked by a later reading than the one that named it.
        /// </summary>
        public void Dropped(int carried)
        {
            Programmes.Programmes.RemoveAll(programme =>
                programme.EventId.Value == carried || programme.EventId.Value == Beside);

            Programme beside = Programme.Rehydrate(
                new ProgrammeId(new NetworkId(Network), new ServiceId(Listed), new EventId(Beside)),
                new TransportStreamId(Carried),
                Now.AddHours(4),
                Now.AddHours(5),
                "the one beside it",
                "a summary",
                false,
                Now,
                lastHeardAt: Now.AddHours(1));

            beside.MarkRevision(++revisions);
            Programmes.Programmes.Add(beside);
        }

        /// <summary>
        /// A reservation a rule made and the guide then took out, put on the shelf as it would be
        /// read back from the ledger, so a window that has since closed can be held against a
        /// programme the guide is announcing again.
        /// </summary>
        public Reservation StandingCancelled(int carried, DateTime startAt, DateTime endAt)
        {
            Programme announced = Programmes.Programmes.Single(programme => programme.EventId.Value == carried);
            Reservation cancelled = Reservation.Rehydrate(
                ReservationId.New(),
                new ProgrammeRef(
                    announced.NetworkId,
                    announced.ServiceId,
                    announced.EventId,
                    announced.StartsAt),
                Rules.Rules[0].Id,
                Priority.Default,
                startAt,
                endAt,
                true,
                Margin.None,
                Margin.None,
                new ProgrammeSnapshot(announced.Name, announced.Summary, string.Empty, [], Now),
                null,
                BroadcastGroupRole.Standalone,
                ReservationState.Cancelled,
                null,
                null,
                false,
                [],
                true,
                null,
                false,
                null,
                Now,
                ReservationCancellation.ProgrammeGone);

            Reservations.Standing(cancelled);

            return cancelled;
        }

        public Reservation BookedByHand(int carried)
        {
            Programme announced = Programmes.Programmes.Single(programme => programme.EventId.Value == carried);
            Reservation booked = ReservationFixtures.Rehydrated(
                ReservationState.Scheduled,
                programme: new ProgrammeRef(
                    announced.NetworkId,
                    announced.ServiceId,
                    announced.EventId,
                    announced.StartsAt),
                startAt: announced.StartsAt,
                endAt: announced.EndsAt);

            Reservations.Standing(booked);

            return booked;
        }
    }
}
