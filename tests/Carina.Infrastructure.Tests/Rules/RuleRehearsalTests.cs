using Carina.Contracts;
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

public sealed class RuleRehearsalTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const int Network = 4;

    private const int Carried = 32_736;

    private const int Listed = 1049;

    private const int Alongside = 1040;

    [Fact]
    public async Task ADraftSaysWhatItWouldTakeWithoutWritingAnything()
    {
        World world = World.Of();
        world.Guide(Broadcast(1, "hill walking"), Broadcast(2, "river fishing"));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(Draft("keyword=hill"), Cancel);

        Assert.NotNull(rehearsed);
        Assert.Equal(["hill walking"], [.. rehearsed.Taking.Select(taken => taken.Programme.Name)]);
        Assert.Single(rehearsed.Making);
        Assert.Empty(world.Reservations.Held);
        Assert.Equal(0, world.Write.Committed);
    }

    [Fact]
    public async Task ADraftWhoseConditionsAreAllEmptyIsNotRehearsedAtAll()
    {
        World world = World.Of();
        world.Guide(Broadcast(1, "hill walking"));

        Assert.Null(await world.Applying.RehearsedAsync(Draft("sort=Name"), Cancel));
    }

    [Fact]
    public async Task AnUnsavedDraftIsWeighedAgainstTheReservationsThatAlreadyStand()
    {
        World world = Crowded(seats: 1);

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(Draft("keyword=hill"), Cancel);

        Assert.NotNull(rehearsed);
        Assert.True(rehearsed.Settled.Settled);
        Assert.Single(rehearsed.Making);
        Assert.Single(rehearsed.Settled.Plan.Contended);
    }

    [Fact]
    public async Task TheSameDraftClashesWithNothingWhenThereIsASeatForItAsWell()
    {
        World world = Crowded(seats: 2);

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(Draft("keyword=hill"), Cancel);

        Assert.NotNull(rehearsed);
        Reservation proposed = Assert.Single(rehearsed.Making);
        Assert.Equal(AllocationVerdict.Secured, rehearsed.Settled.Plan.For(proposed.Id).Verdict);
        Assert.Empty(rehearsed.Settled.Plan.Contended);
    }

    [Fact]
    public async Task WhatAlreadyStandsClashesWithNothingUntilTheDraftIsWeighedBesideIt()
    {
        World world = Crowded(seats: 1);

        SchedulingRun without = await world.Scheduling.PreviewAsync([], Cancel);

        Assert.Empty(without.Plan.Contended);
    }

    [Fact]
    public async Task ABroadcastCarriedAsAShadowIsCountedOutRatherThanTaken()
    {
        World world = World.Of();
        world.Guide(
            Broadcast(1, "hill walking"),
            Broadcast(2, "hill walking", Alongside, shadow: true));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(Draft("keyword=hill"), Cancel);

        Assert.NotNull(rehearsed);
        Assert.Single(rehearsed.Taking);
        Assert.Equal(1, rehearsed.Shadowed);
    }

    [Fact]
    public async Task NothingIsCountedOutWhenNoBroadcastIsCarriedAsAShadow()
    {
        World world = World.Of();
        world.Guide(Broadcast(1, "hill walking"), Broadcast(2, "hill climbing", Alongside));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(Draft("keyword=hill"), Cancel);

        Assert.NotNull(rehearsed);
        Assert.Equal(2, rehearsed.Taking.Count);
        Assert.Equal(0, rehearsed.Shadowed);
    }

    [Fact]
    public async Task AProgrammeAlreadyReservedIsNotCountedAsOneMoreToMake()
    {
        World world = World.Of();
        Programme already = Broadcast(1, "hill walking");
        world.Guide(already, Broadcast(2, "hillside"));
        world.Reservations.Standing(Standing(already, ruleId: RuleId.New()));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(Draft("keyword=hill"), Cancel);

        Assert.NotNull(rehearsed);
        Assert.Equal(2, rehearsed.Taking.Count);
        Assert.Equal(["hillside"], [.. rehearsed.Making.Select(made => made.SnapshotName)]);
        Assert.Single(rehearsed.ChangingHands);
    }

    [Fact]
    public async Task WhatTheDraftItselfAlreadyReservedIsNotCountedAsChangingHands()
    {
        World world = World.Of();
        Rule draft = Draft("keyword=hill");
        world.Rules.Rules.Add(draft);
        Programme already = Broadcast(1, "hill walking");
        world.Guide(already);
        world.Reservations.Standing(Standing(already, ruleId: draft.Id));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(draft, Cancel);

        Assert.NotNull(rehearsed);
        Assert.Empty(rehearsed.Making);
        Assert.Empty(rehearsed.ChangingHands);
        Assert.Empty(rehearsed.Withdrawing);
    }

    [Fact]
    public async Task WhatTheDraftNoLongerTakesIsCountedAsComingBack()
    {
        World world = Collected();
        Rule saved = Draft("keyword=hill");
        world.Rules.Rules.Add(saved);
        Programme leaving = Broadcast(1, "hill walking");
        world.Guide(leaving, Broadcast(2, "river fishing"));
        world.Reservations.Standing(Standing(leaving, ruleId: saved.Id));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(
            Draft("keyword=river", identifier: saved.Id),
            Cancel);

        Assert.NotNull(rehearsed);
        Assert.Equal(["river fishing"], [.. rehearsed.Making.Select(made => made.SnapshotName)]);
        Assert.Equal(["hill walking"], [.. rehearsed.Withdrawing.Select(going => going.SnapshotName)]);
    }

    [Fact]
    public async Task ARuleThatComesFirstKeepsWhatTheDraftWouldOtherwiseTake()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Rule.Draft(
            new RuleId(new Guid("00000009-0000-0000-0000-000000000000")),
            "ahead of the draft",
            new RuleQuery("keyword=hill"),
            new Priority(90),
            true,
            Margin.None,
            Margin.None,
            Now.AddDays(-30)));
        world.Guide(Broadcast(1, "hill walking"));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(
            Draft("keyword=hill", priority: 10),
            Cancel);

        Assert.NotNull(rehearsed);
        Assert.Empty(rehearsed.Taking);
        Assert.Empty(rehearsed.Making);
    }

    [Fact]
    public async Task ADraftThatComesFirstTakesItFromTheRuleBehindIt()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Rule.Draft(
            new RuleId(new Guid("00000009-0000-0000-0000-000000000000")),
            "behind the draft",
            new RuleQuery("keyword=hill"),
            new Priority(10),
            true,
            Margin.None,
            Margin.None,
            Now.AddDays(-30)));
        world.Guide(Broadcast(1, "hill walking"));

        RuleRehearsal? rehearsed = await world.Applying.RehearsedAsync(
            Draft("keyword=hill", priority: 90),
            Cancel);

        Assert.NotNull(rehearsed);
        Assert.Single(rehearsed.Taking);
        Assert.Single(rehearsed.Making);
    }

    private static World Crowded(int seats)
    {
        World world = World.Of(seats);
        DateTime opens = Now.AddHours(2);
        world.Tuning.Answer(Alongside, TuningParameters.Terrestrial(29));
        world.Guide(Broadcast(1, "hill walking", startsAt: opens));
        world.Reservations.Standing(Standing(Broadcast(9, "an outside booking", Alongside, opens)));

        return world;
    }

    private static World Collected()
    {
        World world = World.Of();
        world.Visited(VisitOutcome.Complete);

        return world;
    }

    private static Rule Draft(string query, int priority = 10, RuleId? identifier = null)
        => Rule.Draft(
            identifier ?? new RuleId(new Guid("00000001-0000-0000-0000-000000000000")),
            "a draft",
            new RuleQuery(query),
            new Priority(priority),
            true,
            Margin.None,
            Margin.None,
            Now.AddDays(-1));

    private static Programme Broadcast(
        int carried,
        string name,
        int service = Listed,
        DateTime? startsAt = null,
        bool shadow = false)
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(Carried),
            startsAt ?? Now.AddHours(2 + carried),
            (startsAt ?? Now.AddHours(2 + carried)).AddHours(1),
            name,
            "a summary",
            shadow,
            Now,
            revision: 1);

    private static Reservation Standing(Programme programme, RuleId? ruleId = null)
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
            new ProgrammeSnapshot(programme.Name, programme.Summary, string.Empty, [], Now),
            null,
            BroadcastGroupRole.Standalone,
            ReservationState.Scheduled,
            null,
            null,
            false,
            [],
            false,
            null,
            false,
            null,
            Now);

    private static BroadcastStream Terrestrial(int stream, params int[] services)
        => new(
            new NetworkId(Network),
            new TransportStreamId(stream),
            TuningParameters.Terrestrial(27),
            [.. services.Select(service => new ServiceId(service))]);

    private sealed class World
    {
        private World(int seats)
        {
            Write = new WatchedWrite();
            Reservations = new HeldReservations(Write);
            Streams = new CountedStreams([Terrestrial(Carried, Listed, Alongside)]);
            Seating = new HeldSeating(new TunerCapacity(
                [
                    .. Enumerable.Range(0, seats).Select(index =>
                        new TunerSeat($"seat{index}", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)),
                ],
                []));
            Tuning = new TuningByService { Otherwise = Tunable() };
            Scheduling = new ReservationSchedulingService(
                Reservations,
                Seating,
                Tuning,
                Write,
                RollingHorizon.Default,
                new FixedClock(Now));

            Applying = new RuleApplicationService(
                Rules,
                Programmes,
                Reservations,
                Visits,
                Streams,
                Scheduling,
                new RuleMatcher(new ProgrammeSearchScope(Streams, Services), new FixedClock(Now)),
                new RuleApplicationSettings(),
                Write,
                new FixedClock(Now));
        }

        public HeldRules Rules { get; } = new();

        public HeldProgrammes Programmes { get; } = new();

        public HeldReservations Reservations { get; }

        public HeldStreamVisits Visits { get; } = new();

        public CountedStreams Streams { get; }

        public CountedServices Services { get; } = new();

        public WatchedWrite Write { get; }

        public HeldSeating Seating { get; }

        public TuningByService Tuning { get; }

        public ReservationSchedulingService Scheduling { get; }

        public RuleApplicationService Applying { get; }

        public static World Of(int seats = 3) => new(seats);

        public void Guide(params Programme[] programmes) => Programmes.Programmes.AddRange(programmes);

        public void Visited(VisitOutcome outcome)
            => Visits.Visits.Add(StreamVisit.Record(
                new NetworkId(Network),
                new TransportStreamId(Carried),
                outcome,
                Now.AddHours(-1),
                TimeSpan.FromSeconds(30)));

        private static TuningResolution Tunable()
            => TuningResolution.Tunable(
                new CandidateChannelId(Guid.NewGuid()),
                TuningParameters.Terrestrial(27),
                impaired: false);
    }
}
