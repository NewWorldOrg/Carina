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

public sealed class RuleApplicationServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const int Network = 4;

    private const int Carried = 32_736;

    private const int Beside = 32_737;

    private const int Listed = 1049;

    private const int Alongside = 1040;

    [Fact]
    public async Task ARuleMakesAReservationForTheProgrammeItTakesAndNotForTheOneItLeaves()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill", name: "hills"));
        world.Guide(Broadcast(Listed, 1, "hill walking"), Broadcast(Listed, 2, "river fishing"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Reservation made = Assert.Single(run.Made);
        Assert.Equal("hill walking", made.SnapshotName);
        Assert.Equal(new EventId(1), made.EventId);
        Assert.Equal(["hill walking"], Named(world));
    }

    [Fact]
    public async Task AReservationARuleMadeCarriesTheRuleTheMarginsAndThePriorityItWasWrittenWith()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill", priority: 40, marginBefore: 30, marginAfter: 60);
        world.Rules.Rules.Add(rule);
        world.Guide(Broadcast(Listed, 1, "hill walking"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Reservation made = Assert.Single(run.Made);
        Assert.Equal(rule.Id, made.RuleId);
        Assert.Equal(40, made.Priority.Value);
        Assert.Equal(TimeSpan.FromSeconds(30), made.MarginBefore.Value);
        Assert.Equal(TimeSpan.FromSeconds(60), made.MarginAfter.Value);
        Assert.True(made.IsRuleBorn);
    }

    [Fact]
    public async Task AReservationARuleMadeIsSettledOnTheTunersRatherThanLeftUndecided()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(Listed, 1, "hill walking"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(ReservationState.Scheduled, Assert.Single(run.Made).State);
        Assert.Equal(1, world.Write.Committed);
    }

    [Fact]
    public async Task ARuleThatIsOffMakesNothingThoughTheSameQueryWouldTakeIt()
    {
        World taking = World.Of();
        taking.Rules.Rules.Add(Written("keyword=hill"));
        taking.Guide(Broadcast(Listed, 1, "hill walking"));

        World leaving = World.Of();
        leaving.Rules.Rules.Add(Written("keyword=hill", enabled: false));
        leaving.Guide(Broadcast(Listed, 1, "hill walking"));

        Assert.Single((await taking.Applying.EverythingAsync(Cancel)).Made);
        Assert.Empty((await leaving.Applying.EverythingAsync(Cancel)).Made);
    }

    [Fact]
    public async Task AProgrammeThatIsAlreadyReservedIsNotReservedTwice()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        Programme already = Broadcast(Listed, 1, "hill walking");
        world.Guide(already);
        world.Reservations.Standing(Standing(already, ReservationState.Scheduled, RuleId.New()));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Empty(run.Made);
        Assert.Single(world.Reservations.Held);
    }

    [Fact]
    public async Task AReservationSomebodyCancelledIsNotMadeAgainThoughTheRuleStillTakesTheProgramme()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        Programme cancelled = Broadcast(Listed, 1, "hill walking");
        world.Guide(cancelled, Broadcast(Listed, 2, "hillside"));
        world.Reservations.Standing(Standing(cancelled, ReservationState.Cancelled, rule.Id));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(["hillside"], [.. run.Made.Select(reservation => reservation.SnapshotName)]);
    }

    [Fact]
    public async Task TwoChannelsCarryingTheSameEventNumberEachGetTheirOwnReservation()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(Listed, 7, "hill walking"), Broadcast(Alongside, 7, "hillside"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(2, run.Made.Count);
        Assert.Equal(["hill walking", "hillside"], Named(world));
    }

    [Fact]
    public async Task AnEventNumberUsedAgainAWeekLaterGetsItsOwnReservation()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(
            Broadcast(Listed, 7, "hill walking", startsAt: Now.AddHours(2)),
            Broadcast(Listed, 7, "hillside", startsAt: Now.AddDays(7)));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(2, run.Made.Count);
        Assert.Equal(["hill walking", "hillside"], Named(world));
    }

    [Fact]
    public async Task AProgrammeThatIsAlreadyOverIsNotReservedThoughOneStillToComeIs()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill&from=2026-08-01T00:00:00Z&to=2026-08-30T00:00:00Z"));
        world.Guide(
            Broadcast(Listed, 1, "hill walking", startsAt: Now.AddHours(-4)),
            Broadcast(Listed, 2, "hillside", startsAt: Now.AddHours(2)));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(["hillside"], Named(world));
    }

    [Fact]
    public async Task OnlyWhatChangedPastTheRevisionIsRead()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(
            Broadcast(Listed, 1, "hill walking", revision: 1),
            Broadcast(Listed, 2, "hillside", revision: 7));

        RuleApplicationRun run = await world.Applying.SinceAsync(1, Cancel);

        Assert.Equal(1, run.Read);
        Assert.Equal(7, run.Revision);
        Assert.Equal(["hillside"], Named(world));
    }

    [Fact]
    public async Task ASweepReadsEverythingThoughAnIncrementalRunFromTheSameRevisionReadsNothing()
    {
        World swept = World.Of();
        swept.Rules.Rules.Add(Written("keyword=hill"));
        swept.Guide(Broadcast(Listed, 1, "hill walking", revision: 3));

        World stepped = World.Of();
        stepped.Rules.Rules.Add(Written("keyword=hill"));
        stepped.Guide(Broadcast(Listed, 1, "hill walking", revision: 3));

        Assert.Equal(1, (await swept.Applying.EverythingAsync(Cancel)).Read);
        Assert.Equal(0, (await stepped.Applying.SinceAsync(3, Cancel)).Read);
    }

    [Fact]
    public async Task ARunThatReadsNothingLeavesTheRevisionWhereItWas()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(Listed, 1, "hill walking", revision: 3));

        Assert.Equal(9, (await world.Applying.SinceAsync(9, Cancel)).Revision);
    }

    [Fact]
    public async Task ASweepLargerThanOnePageStillSeesEverythingRatherThanTheFirstPage()
    {
        World world = World.Of(new RuleApplicationSettings { Rows = 2 });
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(
            Broadcast(Listed, 1, "hill one", revision: 1),
            Broadcast(Listed, 2, "hill two", revision: 2),
            Broadcast(Listed, 3, "hill three", revision: 3),
            Broadcast(Listed, 4, "hill four", revision: 4),
            Broadcast(Listed, 5, "hill five", revision: 5));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(5, run.Read);
        Assert.Equal(5, run.Made.Count);
        Assert.Equal(5, run.Revision);
    }

    [Fact]
    public async Task AProgrammeThatVanishedTakesItsReservationWithItOnASweepButNotOnAnIncrementalRun()
    {
        World swept = Vanished();
        World stepped = Vanished();

        RuleApplicationRun sweeping = await swept.Applying.EverythingAsync(Cancel);
        RuleApplicationRun stepping = await stepped.Applying.SinceAsync(500, Cancel);

        Assert.Single(sweeping.Withdrawn);
        Assert.Empty(swept.Reservations.Held);
        Assert.Empty(stepping.Withdrawn);
        Assert.Single(stepped.Reservations.Held);
    }

    [Fact]
    public async Task AProgrammeThatStoppedMatchingTakesItsReservationWithItWhileTheOneStillMatchingKeepsIt()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        Programme kept = Broadcast(Listed, 1, "hill walking", revision: 1);
        Programme drifted = Broadcast(Listed, 2, "river fishing", revision: 2);
        world.Guide(kept, drifted);
        world.Reservations.Standing(
            Standing(kept, ReservationState.Scheduled, rule.Id),
            Standing(drifted, ReservationState.Scheduled, rule.Id));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(["river fishing"], [.. run.Withdrawn.Select(reservation => reservation.SnapshotName)]);
        Assert.Equal(["hill walking"], Named(world));
    }

    [Fact]
    public async Task WithdrawingSendsWhatIsLeftBackThroughTheOnePlaceThatWorksOutTheTuners()
    {
        World withdrawing = Vanished();
        World leaving = Vanished(VisitOutcome.Incomplete);
        Reservation going = withdrawing.Reservations.Held[0];

        await withdrawing.Applying.EverythingAsync(Cancel);
        await leaving.Applying.EverythingAsync(Cancel);

        Assert.Contains($"withdraw {going.Id.Value}", withdrawing.Reservations.Wrote, StringComparer.Ordinal);
        Assert.Equal(1, withdrawing.Seating.Reads);
        Assert.Equal(0, leaving.Seating.Reads);
    }

    [Fact]
    public async Task AReservationNoRuleMadeIsLeftAloneWhileTheRuleBornOneBesideItGoes()
    {
        World world = Vanished();
        Programme gone = Broadcast(Alongside, 9, "somebody asked for this", revision: 4);
        world.Reservations.Standing(Standing(gone, ReservationState.Scheduled, null));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Single(run.Withdrawn);
        Assert.Equal(["somebody asked for this"], Named(world));
    }

    [Fact]
    public async Task AReservationThatIsAlreadyRecordingIsLeftAloneWhileTheOneNotYetStartedGoes()
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        Programme recording = Broadcast(Listed, 1, "hill walking");
        Programme waiting = Broadcast(Alongside, 2, "hillside");
        world.Reservations.Standing(
            Standing(recording, ReservationState.Scheduled, rule.Id, startedAt: Now.AddMinutes(-5)),
            Standing(waiting, ReservationState.Scheduled, rule.Id));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(["hillside"], [.. run.Withdrawn.Select(reservation => reservation.SnapshotName)]);
        Assert.Equal(["hill walking"], Named(world));
    }

    [Theory]
    [InlineData(ReservationState.Scheduled, true)]
    [InlineData(ReservationState.Conflict, true)]
    [InlineData(ReservationState.Cancelled, false)]
    [InlineData(ReservationState.Missed, false)]
    public async Task DroppingARuleTakesWhatIsStandingAndLeavesWhatIsAlreadyARecordOfWhatHappened(
        ReservationState state,
        bool leaves)
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        world.Reservations.Standing(Standing(Broadcast(Listed, 1, "hill walking"), state, rule.Id));

        IReadOnlyList<Reservation> dropped = await world.Applying.DroppedAsync(rule.Id, Cancel);

        Assert.Equal(leaves, dropped.Count is 1);
        Assert.Equal(leaves, world.Reservations.Held.Count is 0);
    }

    [Fact]
    public async Task DroppingARuleLeavesWhatAnotherRuleMade()
    {
        World world = World.Of();
        Rule going = Written("keyword=hill", identifier: 1);
        Rule staying = Written("keyword=river", identifier: 2);
        world.Rules.Rules.Add(going);
        world.Rules.Rules.Add(staying);
        world.Visited(VisitOutcome.Complete);
        world.Reservations.Standing(
            Standing(Broadcast(Listed, 1, "hill walking"), ReservationState.Scheduled, going.Id),
            Standing(Broadcast(Listed, 2, "river fishing"), ReservationState.Scheduled, staying.Id));

        await world.Applying.DroppedAsync(going.Id, Cancel);

        Assert.Equal(["river fishing"], Named(world));
    }

    [Theory]
    [InlineData(VisitOutcome.Complete, true)]
    [InlineData(VisitOutcome.BasicOnly, true)]
    [InlineData(VisitOutcome.Incomplete, false)]
    [InlineData(VisitOutcome.Interrupted, false)]
    [InlineData(VisitOutcome.NoLock, false)]
    [InlineData(VisitOutcome.NoBytes, false)]
    public async Task AReservationGoesOnlyWhenTheStreamItCameFromWasCollectedToTheEnd(
        VisitOutcome outcome,
        bool leaves)
    {
        World world = Vanished(outcome);

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(leaves, run.Withdrawn.Count is 1);
        Assert.Equal(leaves, world.Reservations.Held.Count is 0);
    }

    [Fact]
    public async Task AReservationOnAStreamNobodyHasVisitedStays()
    {
        World world = Vanished();
        world.Visits.Visits.Clear();

        Assert.Empty((await world.Applying.EverythingAsync(Cancel)).Withdrawn);
        Assert.Single(world.Reservations.Held);
    }

    [Fact]
    public async Task AReservationOnAServiceNoStreamCarriesStays()
    {
        World world = Vanished();
        world.Streams.Carried.Clear();
        world.Streams.Carried.Add(Terrestrial(Beside, Alongside));

        Assert.Empty((await world.Applying.EverythingAsync(Cancel)).Withdrawn);
        Assert.Single(world.Reservations.Held);
    }

    [Fact]
    public async Task AReservationStartingInsideTheGraceStaysWhileTheOneOutsideItGoes()
    {
        World world = World.Of(new RuleApplicationSettings { Grace = TimeSpan.FromMinutes(10) });
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        world.Reservations.Standing(
            Standing(Broadcast(Listed, 1, "soon"), ReservationState.Scheduled, rule.Id, startAt: Now.AddMinutes(4)),
            Standing(Broadcast(Listed, 2, "later"), ReservationState.Scheduled, rule.Id, startAt: Now.AddMinutes(40)));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(["later"], [.. run.Withdrawn.Select(reservation => reservation.SnapshotName)]);
        Assert.Equal(["soon"], Named(world));
    }

    [Fact]
    public async Task TheGraceIsMeasuredFromWhenTheRecorderStartsRatherThanWhenTheProgrammeDoes()
    {
        World world = World.Of(new RuleApplicationSettings { Grace = TimeSpan.FromMinutes(10) });
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(VisitOutcome.Complete);
        world.Reservations.Standing(
            Standing(
                Broadcast(Listed, 1, "early bird"),
                ReservationState.Scheduled,
                rule.Id,
                startAt: Now.AddMinutes(15),
                marginBefore: 600));

        Assert.Empty((await world.Applying.EverythingAsync(Cancel)).Withdrawn);
        Assert.Single(world.Reservations.Held);
    }

    [Fact]
    public async Task ARuleSomebodyTurnedOffTakesItsReservationsBackInsideTheGraceToo()
    {
        World standing = World.Of(new RuleApplicationSettings { Grace = TimeSpan.FromMinutes(10) });
        Rule kept = Written("keyword=hill");
        standing.Rules.Rules.Add(kept);
        standing.Visited(VisitOutcome.Complete);
        standing.Reservations.Standing(
            Standing(Broadcast(Listed, 1, "soon"), ReservationState.Scheduled, kept.Id, startAt: Now.AddMinutes(4)));

        World turned = World.Of(new RuleApplicationSettings { Grace = TimeSpan.FromMinutes(10) });
        Rule off = Written("keyword=hill", enabled: false);
        turned.Rules.Rules.Add(off);
        turned.Visited(VisitOutcome.Complete);
        turned.Reservations.Standing(
            Standing(Broadcast(Listed, 1, "soon"), ReservationState.Scheduled, off.Id, startAt: Now.AddMinutes(4)));

        Assert.Empty((await standing.Applying.EverythingAsync(Cancel)).Withdrawn);
        Assert.Single((await turned.Applying.EverythingAsync(Cancel)).Withdrawn);
    }

    [Fact]
    public async Task ARuleNobodyCanFindAnyMoreTakesItsReservationsBackInsideTheGraceToo()
    {
        World world = World.Of(new RuleApplicationSettings { Grace = TimeSpan.FromMinutes(10) });
        world.Visited(VisitOutcome.Complete);
        world.Reservations.Standing(
            Standing(Broadcast(Listed, 1, "soon"), ReservationState.Scheduled, RuleId.New(), startAt: Now.AddMinutes(4)));

        Assert.Single((await world.Applying.EverythingAsync(Cancel)).Withdrawn);
    }

    [Fact]
    public async Task ARuleWhoseQueryCannotBeReadIsTurnedOffAndWrittenDownWhileTheSoundOneKeepsWorking()
    {
        World world = World.Of();
        Rule broken = Written("keyword=h", identifier: 1, name: "one letter", priority: 90);
        Rule sound = Written("keyword=hill", identifier: 2, name: "sound", priority: 10);
        world.Rules.Rules.Add(broken);
        world.Rules.Rules.Add(sound);
        world.Guide(Broadcast(Listed, 1, "hill walking"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal("one letter", Assert.Single(run.TurnedOff).Name);
        Assert.Equal([broken.Id.Value], world.Rules.Saved);
        Assert.False(broken.Enabled);
        Assert.Equal(["hill walking"], Named(world));
    }

    [Fact]
    public async Task ARuleThatCouldNotBeWorkedOutIsReportedRatherThanStoppingTheRunAndTheRestStillWork()
    {
        World world = Broken();
        world.Guide(Broadcast(Listed, 1, "hill walking"), Broadcast(Listed, 2, "river fishing"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal("names a broadcast type", Assert.Single(run.Faulted).Rule.Name);
        Assert.Equal(["river fishing"], Named(world));
    }

    [Fact]
    public async Task ARuleThatCouldNotBeWorkedOutIsNotTurnedOff()
    {
        World world = Broken();
        world.Guide(Broadcast(Listed, 1, "hill walking"));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Empty(run.TurnedOff);
        Assert.Empty(world.Rules.Saved);
        Assert.True(world.Rules.Rules[0].Enabled);
    }

    [Fact]
    public async Task ARuleThatCouldNotBeWorkedOutKeepsItsReservationsWhileASoundRuleLosesTheOnesItDropped()
    {
        World world = Broken();
        world.Visited(VisitOutcome.Complete);
        Rule broken = world.Rules.Rules[0];
        Rule sound = world.Rules.Rules[1];
        world.Reservations.Standing(
            Standing(Broadcast(Listed, 1, "kept by the broken rule"), ReservationState.Scheduled, broken.Id),
            Standing(Broadcast(Alongside, 2, "dropped by the sound rule"), ReservationState.Scheduled, sound.Id));

        RuleApplicationRun run = await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(
            ["dropped by the sound rule"],
            [.. run.Withdrawn.Select(reservation => reservation.SnapshotName)]);
        Assert.Equal(["kept by the broken rule"], Named(world));
    }

    [Fact]
    public async Task TheChannelsAndTheGuideAreReadOnceForAWholeRunRatherThanOncePerRule()
    {
        World world = World.Of();

        for (int identifier = 1; identifier <= 6; identifier++)
        {
            world.Rules.Rules.Add(Written(
                identifier % 2 is 0 ? "keyword=hill" : "keyword=hill&type=IsdbT",
                identifier: identifier));
        }

        world.Guide(Broadcast(Listed, 1, "hill walking"));

        await world.Applying.EverythingAsync(Cancel);

        Assert.Equal(6, world.Rules.Rules.Count);
        Assert.Equal(1, world.Services.Reads);
        Assert.Equal(2, world.Streams.Reads);
    }

    private static string[] Named(World world)
        => [.. world.Reservations.Held.Select(reservation => reservation.SnapshotName).Order(StringComparer.Ordinal)];

    private static World Vanished(VisitOutcome outcome = VisitOutcome.Complete)
    {
        World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited(outcome);
        world.Reservations.Standing(
            Standing(Broadcast(Listed, 1, "hill walking"), ReservationState.Scheduled, rule.Id));

        return world;
    }

    private static World Broken()
    {
        World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill&type=IsdbT", identifier: 1, name: "names a broadcast type", priority: 90));
        world.Rules.Rules.Add(Written("keyword=river", identifier: 2, name: "names no broadcast type", priority: 10));
        world.Streams.Carried.Clear();
        world.Streams.Carried.Add(new BroadcastStream(
            new NetworkId(Network),
            new TransportStreamId(Carried),
            null!,
            [new ServiceId(Listed), new ServiceId(Alongside)]));

        return world;
    }

    private static Rule Written(
        string query,
        int priority = 10,
        int identifier = 1,
        string name = "a rule",
        bool enabled = true,
        int marginBefore = 0,
        int marginAfter = 0)
        => Rule.Draft(
            new RuleId(new Guid($"{identifier:x8}-0000-0000-0000-000000000000")),
            name,
            new RuleQuery(query),
            new Priority(priority),
            enabled,
            Margin.OfSeconds(marginBefore),
            Margin.OfSeconds(marginAfter),
            Now.AddDays(-30));

    private static Programme Broadcast(
        int service,
        int carried,
        string name,
        DateTime? startsAt = null,
        long revision = 1,
        int stream = Carried)
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(service), new EventId(carried)),
            new TransportStreamId(stream),
            startsAt ?? Now.AddHours(2),
            (startsAt ?? Now.AddHours(2)).AddHours(1),
            name,
            "a summary",
            false,
            Now,
            revision: revision);

    private static Reservation Standing(
        Programme programme,
        ReservationState state,
        RuleId? ruleId,
        DateTime? startedAt = null,
        DateTime? startAt = null,
        int marginBefore = 0)
    {
        DateTime opens = startAt ?? programme.StartsAt;

        return Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(programme.NetworkId, programme.ServiceId, programme.EventId, programme.StartsAt),
            ruleId,
            Priority.Default,
            opens,
            opens.AddHours(1),
            true,
            Margin.OfSeconds(marginBefore),
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
    }

    private static BroadcastStream Terrestrial(int stream, params int[] services)
        => new(
            new NetworkId(Network),
            new TransportStreamId(stream),
            TuningParameters.Terrestrial(27),
            [.. services.Select(service => new ServiceId(service))]);

    private sealed class World
    {
        private World(RuleApplicationSettings settings)
        {
            Write = new WatchedWrite();
            Reservations = new HeldReservations(Write);
            Streams = new CountedStreams([Terrestrial(Carried, Listed, Alongside)]);
            Seating = new HeldSeating(new TunerCapacity(
                [
                    new TunerSeat("first", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                    new TunerSeat("second", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                    new TunerSeat("third", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                ],
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
                settings,
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

        public static World Of(RuleApplicationSettings? settings = null) => new(settings ?? new RuleApplicationSettings());

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
