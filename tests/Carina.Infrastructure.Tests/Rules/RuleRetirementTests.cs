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

public sealed class RuleRetirementTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const int Network = 4;

    private const int Carried = 32_736;

    private const int Listed = 1049;

    [Fact]
    public async Task RetiringARuleTakesTheReservationsItMadeWithIt()
    {
        World world = Collected();
        Rule rule = Written();
        world.Rules.Rules.Add(rule);
        world.Reservations.Standing(
            Standing(Broadcast(1), ReservationState.Scheduled, rule.Id),
            Standing(Broadcast(2), ReservationState.Conflict, rule.Id));

        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.NotNull(retired);
        Assert.Empty(world.Reservations.Held);
        Assert.Empty(world.Rules.Rules);
        Assert.Equal(2, retired.Withdrawn.Count + retired.Swept.Count);
    }

    [Fact]
    public async Task AStandingReservationNoCollectionVouchesForStillGoesWhenTheRuleItselfGoes()
    {
        World world = World.Of();
        Rule rule = Written();
        world.Rules.Rules.Add(rule);
        world.Reservations.Standing(Standing(Broadcast(1), ReservationState.Scheduled, rule.Id));

        IReadOnlyList<Reservation> held = await world.Applying.DroppedAsync(rule.Id, Cancel);
        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.Empty(held);
        Assert.NotNull(retired);
        Assert.Single(retired.Swept);
        Assert.Empty(world.Reservations.Held);
    }

    [Fact]
    public async Task WhatTheGuardLetsGoIsWithdrawnRatherThanSwept()
    {
        World world = Collected();
        Rule rule = Written();
        world.Rules.Rules.Add(rule);
        world.Reservations.Standing(Standing(Broadcast(1), ReservationState.Scheduled, rule.Id));

        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.NotNull(retired);
        Assert.Single(retired.Withdrawn);
        Assert.Empty(retired.Swept);
    }

    [Fact]
    public async Task RetiringARuleLeavesTheReservationThatIsBeingRecorded()
    {
        World world = Collected();
        Rule rule = Written();
        world.Rules.Rules.Add(rule);
        Reservation recording = Standing(
            Broadcast(1),
            ReservationState.Scheduled,
            rule.Id,
            startedAt: Now.AddMinutes(-5));
        world.Reservations.Standing(recording);

        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.NotNull(retired);
        Assert.Equal([recording.Id], [.. world.Reservations.Held.Select(held => held.Id)]);
    }

    [Fact]
    public async Task RetiringARuleLeavesTheReservationSomebodyCancelled()
    {
        World world = Collected();
        Rule rule = Written();
        world.Rules.Rules.Add(rule);
        Reservation cancelled = Standing(Broadcast(1), ReservationState.Cancelled, rule.Id);
        world.Reservations.Standing(cancelled);

        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.NotNull(retired);
        Assert.Equal([cancelled.Id], [.. world.Reservations.Held.Select(held => held.Id)]);
    }

    [Fact]
    public async Task RetiringARuleLeavesWhatSomebodyBookedByHandAndWhatAnotherRuleMade()
    {
        World world = Collected();
        Rule rule = Written();
        Rule beside = Written(identifier: 2);
        world.Rules.Rules.Add(rule);
        world.Rules.Rules.Add(beside);
        Reservation byHand = Standing(Broadcast(1), ReservationState.Scheduled, null);
        Reservation byAnother = Standing(Broadcast(2), ReservationState.Scheduled, beside.Id);
        world.Reservations.Standing(byHand, byAnother);

        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.NotNull(retired);
        Assert.Equal(
            [byHand.Id, byAnother.Id],
            [.. world.Reservations.Held.Select(held => held.Id)]);
        Assert.Equal([beside.Id], [.. world.Rules.Rules.Select(held => held.Id)]);
    }

    [Fact]
    public async Task RetiringARuleThatIsNotThereSaysSoAndTouchesNothing()
    {
        World world = Collected();
        Rule rule = Written();
        world.Rules.Rules.Add(rule);
        Reservation standing = Standing(Broadcast(1), ReservationState.Scheduled, rule.Id);
        world.Reservations.Standing(standing);

        RuleRetirement? retired = await world.Applying.RetiredAsync(
            new RuleId(new Guid("0000dead-0000-0000-0000-000000000000")),
            Cancel);

        Assert.Null(retired);
        Assert.Equal([standing.Id], [.. world.Reservations.Held.Select(held => held.Id)]);
        Assert.Single(world.Rules.Rules);
        Assert.Equal(0, world.Write.Committed);
    }

    [Fact]
    public async Task WhatGoesWithTheRuleGoesInOneTransactionWithIt()
    {
        World world = Collected();
        Rule rule = Written();
        world.Rules.Rules.Add(rule);
        world.Reservations.Standing(Standing(Broadcast(1), ReservationState.Scheduled, rule.Id));

        await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.False(world.Write.Open);
        Assert.True(world.Write.Committed > 0);
        Assert.Equal(0, world.Write.RolledBack);
    }

    [Fact]
    public async Task WhatTheSweepTookMakesRoomThatIsSettledOnTheTunersAgain()
    {
        World world = World.Of();
        Rule rule = Written();
        world.Rules.Rules.Add(rule);
        world.Reservations.Standing(Standing(Broadcast(1), ReservationState.Scheduled, rule.Id));

        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.NotNull(retired);
        Assert.Single(retired.Swept);
        Assert.Equal(2, world.Write.Committed);
    }

    [Fact]
    public async Task ARuleThatMadeNothingIsRetiredWithoutSettlingTheTunersAgain()
    {
        World world = World.Of();
        Rule rule = Written();
        world.Rules.Rules.Add(rule);

        RuleRetirement? retired = await world.Applying.RetiredAsync(rule.Id, Cancel);

        Assert.NotNull(retired);
        Assert.Empty(retired.Swept);
        Assert.Empty(retired.Withdrawn);
        Assert.Equal(1, world.Write.Committed);
    }

    private static World Collected()
    {
        World world = World.Of();
        world.Visited(VisitOutcome.Complete);

        return world;
    }

    private static Rule Written(int identifier = 1, bool enabled = true)
        => Rule.Draft(
            new RuleId(new Guid($"{identifier:x8}-0000-0000-0000-000000000000")),
            "a rule",
            new RuleQuery("keyword=hill"),
            Priority.Default,
            enabled,
            Margin.None,
            Margin.None,
            Now.AddDays(-30));

    private static Programme Broadcast(int carried, string name = "hill walking")
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(Listed), new EventId(carried)),
            new TransportStreamId(Carried),
            Now.AddHours(2 + carried),
            Now.AddHours(3 + carried),
            name,
            "a summary",
            false,
            Now,
            revision: 1);

    private static Reservation Standing(
        Programme programme,
        ReservationState state,
        RuleId? ruleId,
        DateTime? startedAt = null)
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
            state,
            startedAt,
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
        private World()
        {
            Write = new WatchedWrite();
            Reservations = new HeldReservations(Write);
            Streams = new CountedStreams([Terrestrial(Carried, Listed)]);
            Seating = new HeldSeating(new TunerCapacity(
                [new TunerSeat("first", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)],
                []));
            Tuning = new TuningByService { Otherwise = Tunable() };

            Applying = new RuleApplicationService(
                Rules,
                Programmes,
                Reservations,
                Visits,
                Streams,
                new ReservationSchedulingService(
                    Reservations,
                    Seating,
                    Tuning,
                    Write,
                    RollingHorizon.Default,
                    new FixedClock(Now)),
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

        public RuleApplicationService Applying { get; }

        public static World Of() => new();

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
