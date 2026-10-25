using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Programmes;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Rules;
using Carina.Infrastructure.Tests.Reservations;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Rules;

public sealed class RuleSimulcastTests
{
    private const int Network = 9001;

    private const int Stream = 9002;

    private const int Main = 901;

    private const int Sub = 902;

    private const int Third = 903;

    private const int Event = 77;

    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task BrRd010ARuleTakingABroadcastTheMainAndTheSubChannelsShareReservesItOnceOnTheMain()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(
            Listing(Main, "hill walking", related: [Shared(Sub), Shared(Third)]),
            Listing(Sub, "hill walking", related: [Shared(Main)]),
            Listing(Third, "hill walking", related: [Shared(Main)]));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Reservation made = Assert.Single(run.Made);
        Assert.Equal(Main, made.ServiceId.Value);
        Assert.Equal(BroadcastGroupRole.Standalone, made.BroadcastGroupRole);
        Assert.Null(made.BroadcastGroupKey);
        Assert.Equal(0, run.Moved);
    }

    [Fact]
    public async Task BrRd010ARuleTakingOneNameFromOneStartToOneEndOnTwoServicesOfAStreamReservesItOnceOnTheMain()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Listing(Sub, "hill walking", carried: 12), Listing(Main, "hill walking", carried: 11));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(Main, Assert.Single(run.Made).ServiceId.Value);
    }

    [Fact]
    public async Task BrRd010ARuleReachingOnlyTheSubChannelReservesTheBroadcastOnTheSubChannelItNamed()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written($"keyword=hill&channel={Network}-{Sub}"));
        world.Guide(
            Listing(Main, "hill walking", related: [Shared(Sub)]),
            Listing(Sub, "hill walking", related: [Shared(Main)]));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(Sub, Assert.Single(run.Made).ServiceId.Value);
    }

    [Fact]
    public async Task BrRd010ARuleNamingTheMainAndTheSubChannelReservesTheBroadcastOnceOnTheMain()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written($"keyword=hill&channel={Network}-{Sub}&channel={Network}-{Main}"));
        world.Guide(
            Listing(Main, "hill walking", related: [Shared(Sub)]),
            Listing(Sub, "hill walking", related: [Shared(Main)]));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(Main, Assert.Single(run.Made).ServiceId.Value);
    }

    [Fact]
    public async Task BrRd010ASubChannelThatTurnsUpNamedOnlyOnceItIsOnTheAirIsNotReservedBesideTheMain()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        Programme main = Listing(Main, "hill walking", startsAt: Now.AddMinutes(-5), revision: 1, related: [Shared(Sub)]);
        world.Guide(
            main,
            Listing(Sub, "hill walking", startsAt: Now.AddMinutes(-5), revision: 9, related: [Shared(Main)]));
        world.Reservations.Standing(Standing(main, rule.Id, startedAt: Now.AddMinutes(-5)));

        RuleApplicationRun run = await world.Applying.SinceAsync(5, Cancel);

        Assert.Empty(run.Made);
        Assert.Equal([Main], world.Reservations.Held.Select(reservation => reservation.ServiceId.Value));
    }

    [Fact]
    public async Task BrRd010ASubChannelReservationARuleMadeBeforeItStartsIsTakenBackAndTheMainStays()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        Programme main = Listing(Main, "hill walking", related: [Shared(Sub)]);
        Programme sub = Listing(Sub, "hill walking", related: [Shared(Main)]);
        world.Guide(main, sub);
        Reservation onTheMain = Standing(main, rule.Id);
        Reservation onTheSub = Standing(sub, rule.Id);
        world.Reservations.Standing(onTheMain, onTheSub);

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal([onTheSub.Id], run.Withdrawn.Select(reservation => reservation.Id));
        Assert.Empty(run.Made);
        Assert.Equal([onTheMain.Id], world.Reservations.Held.Select(reservation => reservation.Id));
    }

    [Fact]
    public async Task BrRd010ASubChannelReservationThatHasStartedOrThatAPersonMadeIsLeftAlone()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        Programme running = Listing(Main, "hill walking", carried: 1, startsAt: Now.AddMinutes(-5), related: [Shared(Sub, 1)]);
        Programme runningOnTheSub = Listing(Sub, "hill walking", carried: 1, startsAt: Now.AddMinutes(-5), related: [Shared(Main, 1)]);
        Programme later = Listing(Main, "hill walking", carried: 2, startsAt: Now.AddHours(5), related: [Shared(Sub, 2)]);
        Programme laterOnTheSub = Listing(Sub, "hill walking", carried: 2, startsAt: Now.AddHours(5), related: [Shared(Main, 2)]);
        world.Guide(running, runningOnTheSub, later, laterOnTheSub);
        Reservation started = Standing(runningOnTheSub, rule.Id, startedAt: Now.AddMinutes(-5));
        Reservation byHand = Standing(laterOnTheSub, ruleId: null);
        world.Reservations.Standing(started, byHand);

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Empty(run.Withdrawn);
        Assert.Contains(started, world.Reservations.Held);
        Assert.Contains(byHand, world.Reservations.Held);
    }

    [Theory]
    [InlineData("priority")]
    [InlineData("margin before")]
    [InlineData("margin after")]
    [InlineData("encoding")]
    public async Task BrRd010ASubChannelReservationARuleMadeAndAPersonChangedStaysAndTheMainIsNotReservedBesideIt(string changed)
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        Programme sub = Listing(Sub, "hill walking", related: [Shared(Main)]);
        world.Guide(Listing(Main, "hill walking", related: [Shared(Sub)]), sub);
        Reservation onTheSub = Standing(sub, rule.Id);
        Changed(onTheSub, changed);
        world.Reservations.Standing(onTheSub);

        RuleApplicationRun swept = await world.Applying.EverythingAsync(Cancel);
        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(rule, Cancel);

        Assert.Empty(swept.Withdrawn);
        Assert.Empty(swept.Made);
        Assert.Equal([onTheSub.Id], world.Reservations.Held.Select(reservation => reservation.Id));
        Assert.Empty(rehearsed!.Making);
        Assert.Empty(rehearsed.Withdrawing);
    }

    private static void Changed(Reservation reservation, string what)
    {
        switch (what)
        {
            case "priority":
                reservation.Reprioritise(new Priority(40));
                break;
            case "margin before":
                reservation.Remargin(Margin.OfSeconds(60), Margin.None);
                break;
            case "margin after":
                reservation.Remargin(Margin.None, Margin.OfSeconds(60));
                break;
            default:
                reservation.Rewish(false);
                break;
        }
    }

    private static RelatedProgramme Shared(int service, int carried = Event)
        => new(Network, service, carried, RelationKind.Shared);

    private static Programme Listing(
        int service,
        string name,
        int carried = Event,
        DateTime? startsAt = null,
        long revision = 1,
        IReadOnlyList<RelatedProgramme>? related = null)
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(Stream),
            startsAt ?? Now.AddHours(2),
            (startsAt ?? Now.AddHours(2)).AddHours(1),
            name,
            "a summary",
            false,
            Now,
            related: related,
            revision: revision);

    private static Rule Written(string query)
        => Rule.Draft(
            new RuleId(new Guid("00000001-0000-0000-0000-000000000000")),
            "a rule",
            new RuleQuery(query),
            new Priority(10),
            true,
            Margin.None,
            Margin.None,
            Now.AddDays(-30),
            true);

    private static Reservation Standing(Programme programme, RuleId? ruleId, DateTime? startedAt = null)
        => Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(programme.NetworkId, programme.ServiceId, programme.EventId, programme.StartsAt),
            ruleId,
            Priority.Default,
            programme.StartsAt,
            programme.StartsAt.AddHours(1),
            true,
            Margin.None,
            Margin.None,
            new ProgrammeSnapshot(
                programme.Name,
                programme.Summary,
                string.Empty,
                [],
                Now,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
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
            Now,
            null);

    private sealed class World
    {
        private World()
        {
            Write = new WatchedWrite();
            Outcomes = new HeldOutcomes(Write);
            Reservations = new HeldReservations(Write, Outcomes);
            Streams = new CountedStreams(
            [
                new BroadcastStream(
                    new NetworkId(Network),
                    new TransportStreamId(Stream),
                    TuningParameters.Terrestrial(27),
                    [new ServiceId(Main), new ServiceId(Sub), new ServiceId(Third)]),
            ]);
            HeldSeating seating = new(new TunerCapacity(
                [
                    new TunerSeat("first", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                    new TunerSeat("second", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                ],
                []));
            TuningByService tuning = new()
            {
                Otherwise = TuningResolution.Tunable(
                    new CandidateChannelId(Guid.NewGuid()),
                    TuningParameters.Terrestrial(27),
                    impaired: false),
            };

            Applying = new RuleApplicationService(
                Rules,
                Programmes,
                Reservations,
                Outcomes,
                Visits,
                Epochs,
                Streams,
                new ReservationSchedulingService(
                    Reservations,
                    new NoRecordings(),
                    seating,
                    tuning,
                    Write,
                    RollingHorizon.Default,
                    new SilentEvents(),
                    new FixedClock(Now)),
                new RuleMatcher(new ProgrammeSearchScope(Streams, new CountedServices()), new FixedClock(Now)),
                new RuleApplicationSettings(),
                Write,
                new SilentEvents(),
                new FixedClock(Now));
        }

        public HeldRules Rules { get; } = new();

        public HeldProgrammes Programmes { get; } = new();

        public HeldOutcomes Outcomes { get; }

        public HeldReservations Reservations { get; }

        public HeldStreamVisits Visits { get; } = new();

        public HeldEpochs Epochs { get; } = new();

        public CountedStreams Streams { get; }

        public WatchedWrite Write { get; }

        public RuleApplicationService Applying { get; }

        public static World Of() => new();

        public void Guide(params Programme[] programmes) => Programmes.Programmes.AddRange(programmes);

        public void Visited(VisitOutcome outcome)
            => Visits.Visits.Add(StreamVisit.Record(
                new NetworkId(Network),
                new TransportStreamId(Stream),
                outcome,
                Now.AddHours(-1),
                TimeSpan.FromSeconds(30)));
    }
}
