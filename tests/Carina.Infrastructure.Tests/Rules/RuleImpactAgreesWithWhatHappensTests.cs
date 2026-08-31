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

public sealed class RuleImpactAgreesWithWhatHappensTests
{
    private static readonly DateTime Now = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan LongEnoughToTellAStallFromAWait = TimeSpan.FromSeconds(30);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task WhatTheImpactSaysDeletingWouldSweepIsWhatDeletingSweeps()
    {
        World asked = World.Seeded();
        World acting = World.Seeded();

        RuleRehearsal? rehearsed = await Within(asked.Applying.RehearsedAsync(asked.Rewritten(), Cancel));
        RuleRetirement? retired = await Within(acting.Applying.RetiredAsync(World.TheRule, Cancel));

        Assert.NotNull(rehearsed);
        Assert.NotNull(retired);
        Assert.Equal(Named(retired.Withdrawn.Concat(retired.Swept)), Named(rehearsed.Sweeping));
    }

    [Fact]
    public async Task WhatTheImpactSaysSavingWouldWithdrawIsWhatTheApplicationAfterSavingWithdraws()
    {
        World asked = World.Seeded();
        World acting = World.Seeded();

        RuleRehearsal? rehearsed = await Within(asked.Applying.RehearsedAsync(asked.Rewritten(), Cancel));
        acting.Rewrite();
        RuleApplicationRun ran = await Within(acting.Applying.EverythingAsync(Cancel));

        Assert.NotNull(rehearsed);
        Assert.Empty(ran.Faulted);
        Assert.Equal(Named(ran.Withdrawn), Named(rehearsed.Withdrawing));
    }

    [Fact]
    public async Task SavingIsAnsweredWithTheseAndDeletingWithThoseSoNeitherAnswerIsEmptyNorTheSame()
    {
        World world = World.Seeded();

        RuleRehearsal? rehearsed = await Within(world.Applying.RehearsedAsync(world.Rewritten(), Cancel));

        Assert.NotNull(rehearsed);
        Assert.Equal(
            [
                World.WhatOnlyTheBasicTableVouchesFor,
                World.WhatStartsJustBeyondTheGrace,
                World.WhatCollectionVouchesFor,
            ],
            Named(rehearsed.Withdrawing));
        Assert.Equal(
            [
                World.WhatStartsExactlyOnTheGrace,
                World.WhatAnotherRuleTakesOver,
                World.WhatOnlyTheBasicTableVouchesFor,
                World.WhatStartsJustBeyondTheGrace,
                World.WhatStartsInsideTheGrace,
                World.WhatWasNeverVisitedAtAll,
                World.WhatCollectionVouchesFor,
                World.WhatTheCollectionNeverFinished,
            ],
            Named(rehearsed.Sweeping));
    }

    [Fact]
    public async Task WhatStartsExactlyOnTheGraceIsHeldBackAndWhatStartsASecondLaterIsNot()
    {
        World world = World.Seeded();

        RuleRehearsal? rehearsed = await Within(world.Applying.RehearsedAsync(world.Rewritten(), Cancel));

        Assert.NotNull(rehearsed);
        Assert.DoesNotContain(World.WhatStartsExactlyOnTheGrace, Named(rehearsed.Withdrawing));
        Assert.Contains(World.WhatStartsJustBeyondTheGrace, Named(rehearsed.Withdrawing));
        Assert.Contains(World.WhatStartsExactlyOnTheGrace, Named(rehearsed.Sweeping));
    }

    [Fact]
    public async Task SwitchingTheRuleOffTakesWhatStartsInsideTheGraceThoughAnApplicationLeavesIt()
    {
        World switching = World.Seeded();
        World applying = World.Seeded();

        IReadOnlyList<Reservation> dropped =
            await Within(switching.Applying.DroppedAsync(World.TheRule, Cancel));

        applying.Rewrite();
        RuleApplicationRun ran = await Within(applying.Applying.EverythingAsync(Cancel));

        Assert.Contains(World.WhatStartsInsideTheGrace, Named(dropped));
        Assert.Contains(World.WhatStartsExactlyOnTheGrace, Named(dropped));
        Assert.DoesNotContain(World.WhatStartsInsideTheGrace, Named(ran.Withdrawn));
        Assert.DoesNotContain(World.WhatStartsExactlyOnTheGrace, Named(ran.Withdrawn));
        Assert.Contains(World.WhatCollectionVouchesFor, Named(dropped));
        Assert.Contains(World.WhatCollectionVouchesFor, Named(ran.Withdrawn));
    }

    [Fact]
    public async Task SwitchingTheRuleOffLeavesWhatNoCollectionVouchesForThoughDeletingTakesIt()
    {
        World switching = World.Seeded();
        World deleting = World.Seeded();

        IReadOnlyList<Reservation> dropped =
            await Within(switching.Applying.DroppedAsync(World.TheRule, Cancel));

        RuleRetirement? retired = await Within(deleting.Applying.RetiredAsync(World.TheRule, Cancel));

        Assert.NotNull(retired);
        Assert.DoesNotContain(World.WhatTheCollectionNeverFinished, Named(dropped));
        Assert.DoesNotContain(World.WhatWasNeverVisitedAtAll, Named(dropped));
        Assert.Contains(World.WhatTheCollectionNeverFinished, Named(retired.Swept));
        Assert.Contains(World.WhatWasNeverVisitedAtAll, Named(retired.Swept));
    }

    [Fact]
    public async Task TheRuleTakesTheseBroadcastsBeforeItIsRewrittenToTakeNoneOfThem()
    {
        World world = World.Seeded();

        RuleRehearsal? asItStands = await Within(world.Applying.RehearsedAsync(world.AsItStands(), Cancel));
        RuleRehearsal? rewritten = await Within(world.Applying.RehearsedAsync(world.Rewritten(), Cancel));

        Assert.NotNull(asItStands);
        Assert.NotNull(rewritten);
        Assert.Contains(
            World.WhatAnotherRuleTakesOver,
            asItStands.Taking.Select(take => take.Programme.Name).ToList());
        Assert.Empty(rewritten.Taking);
    }

    private static IReadOnlyList<string> Named(IEnumerable<Reservation> reservations)
        => [.. reservations.Select(reservation => reservation.SnapshotName).Order(StringComparer.Ordinal)];

    private static Task<T> Within<T>(Task<T> asked) => asked.WaitAsync(LongEnoughToTellAStallFromAWait);

    private sealed class World
    {
        public const string WhatCollectionVouchesFor = "hill walking";

        public const string WhatStartsInsideTheGrace = "hill running";

        public const string WhatStartsExactlyOnTheGrace = "hill ambling";

        public const string WhatStartsJustBeyondTheGrace = "hill roving";

        public const string WhatOnlyTheBasicTableVouchesFor = "hill climbing";

        public const string WhatTheCollectionNeverFinished = "hill wandering";

        public const string WhatWasNeverVisitedAtAll = "hill scrambling";

        public const string WhatIsBeingRecorded = "hill rambling";

        public const string WhatSomebodyCancelled = "hill striding";

        public const string WhatSomebodyBookedByHand = "hill trekking";

        public const string WhatAnotherRuleTakesOver = "hill bounding";

        public const string WhatAnotherRuleMade = "river fishing";

        public static readonly RuleId TheRule = new(new Guid("00000001-0000-0000-0000-000000000000"));

        public static readonly RuleId Beside = new(new Guid("00000002-0000-0000-0000-000000000000"));

        public static readonly RuleId Waiting = new(new Guid("00000003-0000-0000-0000-000000000000"));

        private const int Network = 4;

        private const int Vouched = 32_736;

        private const int OnlyBasic = 32_737;

        private const int Unfinished = 32_738;

        private const int Unvisited = 32_739;

        private const int Listed = 1049;

        private const int Alongside = 1040;

        private const int Basic = 1050;

        private const int Partial = 1051;

        private const int Unseen = 1052;

        private const int Ahead = 90;

        private const int Behind = 10;

        private World()
        {
            Write = new WatchedWrite();
            Reservations = new HeldReservations(Write);
            Streams = new CountedStreams(
            [
                Terrestrial(Vouched, 27, Listed, Alongside),
                Terrestrial(OnlyBasic, 29, Basic),
                Terrestrial(Unfinished, 31, Partial),
                Terrestrial(Unvisited, 33, Unseen),
            ]);
            Seating = new HeldSeating(new TunerCapacity(
                [
                    .. Enumerable.Range(0, 9).Select(index =>
                        new TunerSeat($"seat{index}", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)),
                ],
                []));
            Tuning = new TuningByService
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
                Settings,
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

        public RuleApplicationSettings Settings { get; } = new();

        public RuleApplicationService Applying { get; }

        public static World Seeded()
        {
            var world = new World();

            world.Rules.Rules.Add(Written(TheRule, "keyword=hill", Ahead));
            world.Rules.Rules.Add(Written(Beside, "keyword=river", Behind));
            world.Rules.Rules.Add(Written(Waiting, "keyword=bounding", Behind));

            world.Visited(Vouched, VisitOutcome.Complete);
            world.Visited(OnlyBasic, VisitOutcome.BasicOnly);
            world.Visited(Unfinished, VisitOutcome.Incomplete);

            world.Standing(1, WhatCollectionVouchesFor, Listed, Vouched, Now.AddHours(2), TheRule);
            world.Standing(2, WhatStartsInsideTheGrace, Alongside, Vouched, Now.AddMinutes(4), TheRule);
            world.Standing(
                3,
                WhatStartsExactlyOnTheGrace,
                Alongside,
                Vouched,
                Now + world.Settings.Grace,
                TheRule);
            world.Standing(
                4,
                WhatStartsJustBeyondTheGrace,
                Alongside,
                Vouched,
                Now + world.Settings.Grace + TimeSpan.FromSeconds(1),
                TheRule);
            world.Standing(5, WhatOnlyTheBasicTableVouchesFor, Basic, OnlyBasic, Now.AddHours(3), TheRule);
            world.Standing(6, WhatTheCollectionNeverFinished, Partial, Unfinished, Now.AddHours(4), TheRule);
            world.Standing(7, WhatWasNeverVisitedAtAll, Unseen, Unvisited, Now.AddHours(5), TheRule);
            world.Standing(
                8,
                WhatIsBeingRecorded,
                Listed,
                Vouched,
                Now.AddMinutes(-20),
                TheRule,
                startedAt: Now.AddMinutes(-20));
            world.Standing(
                9,
                WhatSomebodyCancelled,
                Listed,
                Vouched,
                Now.AddHours(6),
                TheRule,
                state: ReservationState.Cancelled);
            world.Standing(10, WhatSomebodyBookedByHand, Listed, Vouched, Now.AddHours(7), null);
            world.Standing(11, WhatAnotherRuleMade, Listed, Vouched, Now.AddHours(8), Beside);
            world.Standing(12, WhatAnotherRuleTakesOver, Listed, Vouched, Now.AddHours(9), TheRule);

            return world;
        }

        public Rule AsItStands() => Written(TheRule, "keyword=hill", Ahead);

        public Rule Rewritten() => Written(TheRule, "keyword=heather", Ahead);

        public void Rewrite()
            => Rules.Rules
                .Single(rule => rule.Id.Equals(TheRule))
                .Rewrite("a rule", new RuleQuery("keyword=heather"), new Priority(Ahead), Margin.None, Margin.None);

        private static Rule Written(RuleId id, string query, int priority)
            => Rule.Draft(
                id,
                "a rule",
                new RuleQuery(query),
                new Priority(priority),
                true,
                Margin.None,
                Margin.None,
                Now.AddDays(-30));

        private static BroadcastStream Terrestrial(int stream, int channel, params int[] services)
            => new(
                new NetworkId(Network),
                new TransportStreamId(stream),
                TuningParameters.Terrestrial(channel),
                [.. services.Select(service => new ServiceId(service))]);

        private void Visited(int stream, VisitOutcome outcome)
            => Visits.Visits.Add(StreamVisit.Record(
                new NetworkId(Network),
                new TransportStreamId(stream),
                outcome,
                Now.AddHours(-1),
                TimeSpan.FromSeconds(30)));

        private Programme Broadcast(int carried, string name, int service, int stream, DateTime startsAt)
            => Programme.Rehydrate(
                new ProgrammeId(new NetworkId(Network), new ServiceId(service), new EventId(carried)),
                new TransportStreamId(stream),
                startsAt,
                startsAt.AddHours(1),
                name,
                "a summary",
                false,
                Now,
                revision: carried);

        private void Standing(
            int carried,
            string name,
            int service,
            int stream,
            DateTime startsAt,
            RuleId? ruleId,
            DateTime? startedAt = null,
            ReservationState state = ReservationState.Scheduled)
        {
            Programme programme = Broadcast(carried, name, service, stream, startsAt);

            Programmes.Programmes.Add(programme);
            Reservations.Standing(Reservation.Rehydrate(
                new ReservationId(new Guid($"{carried:x8}-0000-0000-0000-00000000000f")),
                new ProgrammeRef(programme.NetworkId, programme.ServiceId, programme.EventId, programme.StartsAt),
                ruleId,
                Priority.Default,
                programme.StartsAt,
                programme.StartsAt.AddHours(1),
                true,
                Margin.None,
                Margin.None,
                new ProgrammeSnapshot(name, "a summary", string.Empty, [], Now),
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
                Now));
        }
    }
}
