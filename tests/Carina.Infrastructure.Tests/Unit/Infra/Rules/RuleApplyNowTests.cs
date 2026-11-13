using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Rules;
using Carina.Infrastructure.Tests.Fixtures.Reservations;
using Carina.Infrastructure.Tests.Fixtures.Rules;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Puppis.Contracts;

namespace Carina.Infrastructure.Tests.Unit.Infra.Rules;

[Trait("Category", "Unit")]
public sealed class RuleApplyNowTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan LongEnoughToTellAStallFromAWait = TimeSpan.FromSeconds(10);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan LongEnoughToTellAStallFromAWaitForTheRun = TimeSpan.FromSeconds(30);

    private static readonly DateTime NowForTheRun = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AskingForTheRulesToBeAppliedRunsAPassAndAnswersWithWhatItDid()
    {
        var passes = new CountedPasses();
        RuleApplyNow applying = Applying(passes);

        RuleApplyOutcome outcome = await Within(applying.StartAsync(Cancel));

        Assert.Null(outcome.Refusal);
        Assert.NotNull(outcome.Run);
        Assert.Equal(1, passes.Ran);
        Assert.Equal([RecalculationTrigger.RulesChanged], passes.Asked);
        Assert.Same(passes.Answered, outcome.Run.Pass);
    }

    [Fact]
    public async Task WhatIsAskedForReachesEverythingRatherThanOnlyTheTuners()
    {
        Assert.Equal(
            RecalculationReach.Everything,
            RecalculationReaches.Of(RecalculationTrigger.RulesChanged));
    }

    [Fact]
    public async Task ASecondApplicationWhileTheFirstIsWalkingIsRefusedAndNamesTheOneWalking()
    {
        var passes = new CountedPasses { Held = new TaskCompletionSource() };
        RuleApplyNow applying = Applying(passes);

        Task<RuleApplyOutcome> first = applying.StartAsync(Cancel);
        await Within(passes.Entered.Task);

        RuleApplyOutcome second = await Within(applying.StartAsync(Cancel));

        passes.Held.SetResult();

        RuleApplyOutcome finished = await Within(first);

        Assert.Null(second.Run);
        Assert.NotNull(second.Refusal);
        Assert.Equal(RuleApplyRefusal.OneIsAlreadyRunning, second.Refusal.Refusal);
        Assert.NotNull(finished.Run);
        Assert.Equal(finished.Run.ApplyId, second.Refusal.RunningId);
        Assert.Equal(1, passes.Ran);
    }

    [Fact]
    public async Task NoTwoApplicationsAreEverInsideThePassAtTheSameTime()
    {
        var passes = new CountedPasses();
        RuleApplyNow applying = Applying(passes);

        Task<RuleApplyOutcome>[] asked =
        [
            .. Enumerable.Range(0, 8).Select(_ => Task.Run(() => applying.StartAsync(Cancel), Cancel)),
        ];

        await Within(Task.WhenAll(asked));

        Assert.Equal(1, passes.Deepest);
        Assert.True(passes.Ran >= 1);
    }

    [Fact]
    public async Task AnApplicationTooSoonAfterTheLastOneIsRefusedWithTheMomentItMayBeAskedForAgain()
    {
        var clock = new MovingClock(Now);
        var passes = new CountedPasses();
        RuleApplyNow applying = Applying(passes, clock: clock);

        await Within(applying.StartAsync(Cancel));
        clock.Move(TimeSpan.FromSeconds(30));

        RuleApplyOutcome refused = await Within(applying.StartAsync(Cancel));

        Assert.NotNull(refused.Refusal);
        Assert.Equal(RuleApplyRefusal.TooSoonAfterTheLastOne, refused.Refusal.Refusal);
        Assert.Equal(Now + RuleApplySettings.DefaultBetweenApplications, refused.Refusal.NotBefore);
        Assert.Equal(1, passes.Ran);
    }

    [Fact]
    public async Task AnApplicationAskedForOnceTheCooldownIsUpRunsAgain()
    {
        var clock = new MovingClock(Now);
        var passes = new CountedPasses();
        RuleApplyNow applying = Applying(passes, clock: clock);

        await Within(applying.StartAsync(Cancel));
        clock.Move(RuleApplySettings.DefaultBetweenApplications);

        RuleApplyOutcome again = await Within(applying.StartAsync(Cancel));

        Assert.Null(again.Refusal);
        Assert.Equal(2, passes.Ran);
    }

    [Fact]
    public async Task AStandingRecalculationHoldingTheFloorIsAnsweredAsSuchRatherThanWaitedOn()
    {
        var passes = new CountedPasses
        {
            Answers = RecalculationPass.Refused(RecalculationRefusal.OneIsAlreadyRunning),
        };
        RuleApplyNow applying = Applying(passes);

        RuleApplyOutcome outcome = await Within(applying.StartAsync(Cancel));

        Assert.Null(outcome.Run);
        Assert.NotNull(outcome.Refusal);
        Assert.Equal(RuleApplyRefusal.ARecalculationIsAlreadyRunning, outcome.Refusal.Refusal);
    }

    [Fact]
    public async Task APassThatFailsLeavesTheFloorFreeForTheNextAsking()
    {
        var clock = new MovingClock(Now);
        var passes = new CountedPasses { Throws = new InvalidOperationException("the pass failed") };
        RuleApplyNow applying = Applying(passes, clock: clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Within(applying.StartAsync(Cancel)));

        clock.Move(RuleApplySettings.DefaultBetweenApplications);
        passes.Throws = null;

        Assert.Null((await Within(applying.StartAsync(Cancel))).Refusal);
    }

    [Fact]
    public async Task TheCountsAnsweredAreTheOnesFromThePassThatMadeTheReservations()
    {
        using World world = World.Taking(3);

        RuleApplyOutcome outcome = await WithinForTheRun(world.Applying.StartAsync(Cancel));

        Assert.Null(outcome.Refusal);
        Assert.NotNull(outcome.Run);
        Assert.Equal(3, world.Reservations.Held.Count);
        Assert.NotNull(outcome.Run.Pass.Applied);
        Assert.Equal(3, outcome.Run.Pass.Applied.Made.Count);
        Assert.Contains(RecalculationTrigger.RulesChanged, outcome.Run.Pass.Answering);
    }

    [Fact]
    public async Task TheCountsAreStillTheOnesFromItsOwnPassWhenSomebodyElseJustEmptiedWhatWasAskedFor()
    {
        using World world = World.Taking(3);

        world.Recalculating.Nudge(RecalculationTrigger.SelectedChannelChanged);
        await WithinForTheRun(world.Recalculating.RunAsync(Cancel));

        RuleApplyOutcome outcome = await WithinForTheRun(world.Applying.StartAsync(Cancel));

        Assert.Equal(3, world.Reservations.Held.Count);
        Assert.NotNull(outcome.Run?.Pass.Applied);
        Assert.Equal(3, outcome.Run.Pass.Applied.Made.Count);
    }

    [Fact]
    public async Task WhatIsAnsweredAsMadeIsWhatTheLedgerGained()
    {
        using World world = World.Taking(3);
        int before = world.Reservations.Held.Count;

        RuleApplyOutcome outcome = await WithinForTheRun(world.Applying.StartAsync(Cancel));

        Assert.NotNull(outcome.Run?.Pass.Applied);
        Assert.Equal(world.Reservations.Held.Count - before, outcome.Run.Pass.Applied.Made.Count);
        Assert.True(outcome.Run.Pass.Applied.Made.Count > 0, "the pass made nothing, so nothing was measured");
    }

    [Fact]
    public async Task WhatIsAnsweredAsWithdrawnIsWhatTheLedgerLost()
    {
        using World world = World.Losing(2);
        int before = world.Reservations.Held.Count;

        RuleApplyOutcome outcome = await WithinForTheRun(world.Applying.StartAsync(Cancel));

        Assert.NotNull(outcome.Run?.Pass.Applied);
        Assert.Equal(before - world.Reservations.Held.Count, outcome.Run.Pass.Applied.Withdrawn.Count);
        Assert.True(outcome.Run.Pass.Applied.Withdrawn.Count > 0, "the pass withdrew nothing, so nothing was measured");
    }

    [Fact]
    public async Task AnApplicationWhileAPassIsAlreadyWalkingIsRefusedRatherThanAnsweredWithNothing()
    {
        using World world = World.Taking(1);
        world.Seating.Hold = new TaskCompletionSource();

        world.Recalculating.Nudge(RecalculationTrigger.PeriodicReconciliation);
        Task<RecalculationPass> walking = world.Recalculating.RunAsync(Cancel);

        await world.Seating.Arrived.Task.WaitAsync(LongEnoughToTellAStallFromAWaitForTheRun);

        RuleApplyOutcome outcome = await WithinForTheRun(world.Applying.StartAsync(Cancel));

        world.Seating.Hold.SetResult();
        await walking.WaitAsync(LongEnoughToTellAStallFromAWaitForTheRun);

        Assert.Null(outcome.Run);
        Assert.NotNull(outcome.Refusal);
        Assert.Equal(RuleApplyRefusal.ARecalculationIsAlreadyRunning, outcome.Refusal.Refusal);
    }

    private static RuleApplyNow Applying(CountedPasses passes, MovingClock? clock = null)
        => new(passes, new RuleApplySettings(), clock ?? new MovingClock(Now));

    private static Task<T> Within<T>(Task<T> asked) => asked.WaitAsync(LongEnoughToTellAStallFromAWait);

    private static Task Within(Task asked) => asked.WaitAsync(LongEnoughToTellAStallFromAWait);

    private sealed class MovingClock(DateTime from) : TimeProvider
    {
        private DateTime now = from;

        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);

        public void Move(TimeSpan by) => now += by;
    }

    private sealed class CountedPasses : IRecalculationPass
    {
        private readonly List<RecalculationTrigger> asked = [];

        private int inside;

        public int Ran { get; private set; }

        public RecalculationPass? Answered { get; private set; }

        public IReadOnlyList<RecalculationTrigger> Asked
        {
            get
            {
                lock (asked)
                {
                    return [.. asked];
                }
            }
        }

        public int Deepest { get; private set; }

        public TaskCompletionSource? Held { get; init; }

        public TaskCompletionSource Entered { get; } = new();

        public RecalculationPass? Answers { get; init; }

        public Exception? Throws { get; set; }

        public async Task<RecalculationPass> RunAsync(
            RecalculationTrigger asking,
            CancellationToken cancellationToken)
        {
            int depth = Interlocked.Increment(ref inside);

            lock (this)
            {
                Ran++;
                Deepest = Math.Max(Deepest, depth);
            }

            lock (asked)
            {
                asked.Add(asking);
            }

            Entered.TrySetResult();

            try
            {
                if (Held is { } gate)
                {
                    await gate.Task.WaitAsync(LongEnoughToTellAStallFromAWait, cancellationToken);
                }

                if (Throws is { } failure)
                {
                    throw failure;
                }

                RecalculationPass answering = Answers ?? RecalculationPass.Of(
                    [asking],
                    RecalculationReaches.Of(asking),
                    7,
                    new RuleApplicationRun(7, 0, [], [], [], [], [], []),
                    null,
                    null,
                    null,
                    []);

                Answered = answering;

                return answering;
            }
            finally
            {
                Interlocked.Decrement(ref inside);
            }
        }
    }

    private static Task<T> WithinForTheRun<T>(Task<T> asked) => asked.WaitAsync(LongEnoughToTellAStallFromAWaitForTheRun);

    private sealed class World : IDisposable
    {
        private const int Network = 4;

        private const int Carried = 32_736;

        private const int Listed = 1049;

        private readonly ServiceProvider provider;

        private World()
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
            Seating = new GatedSeating(
                new TunerCapacity(
                    [
                        .. Enumerable.Range(0, 6).Select(index => new TunerSeat(
                            $"seat{index}",
                            BroadcastReception.Of(TunerKind.Terrestrial),
                            Faulted: false)),
                    ],
                    []),
                throws: false);
            Tuning = new TuningByService
            {
                Otherwise = TuningResolution.Tunable(
                    new CandidateChannelId(Guid.NewGuid()),
                    TuningParameters.Terrestrial(27),
                    impaired: false),
            };

            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(new FixedClock(NowForTheRun));
            services.AddSingleton<IRuleRepository>(Rules);
            services.AddSingleton<IProgrammeRepository>(Programmes);
            services.AddSingleton<IReservationRepository>(Reservations);
            services.AddSingleton<IRecordingRepository>(new NoRecordings());
            services.AddSingleton<IReservationOutcomeRepository>(Outcomes);
            services.AddSingleton<IStreamVisitRepository>(Visits);
            services.AddSingleton<ICollectionEpochRepository>(new HeldEpochs());
            services.AddSingleton<IBroadcastStreamDirectory>(Streams);
            services.AddSingleton<IBroadcastServiceRepository>(Services);
            services.AddSingleton<ITunerCapacityDirectory>(Seating);
            services.AddSingleton<IServiceTuningDirectory>(Tuning);
            services.AddSingleton<IAtomicWrite>(Write);
            services.AddSingleton<IAppEventPublisher>(new SilentEvents());
            services.AddSingleton(RollingHorizon.Default);
            services.AddSingleton(new RuleApplicationSettings());
            services.AddScoped<ProgrammeSearchScopeDomainService>();
            services.AddScoped<RuleMatcher>();
            services.AddScoped<ReservationOutcomeDomainService>();
            services.AddScoped<ReservationSchedulingService>();
            services.AddScoped<RuleWithdrawalDomainService>();
            services.AddScoped<RuleTakingDomainService>();
            services.AddScoped<RuleApplicationService>();

            provider = services.BuildServiceProvider();

            Recalculating = new ReservationRecalculationHostedService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new RecalculationSettings
                {
                    BeforeFirstPass = TimeSpan.FromHours(1),
                    BetweenReconciliations = TimeSpan.FromHours(1),
                },
                new FixedClock(NowForTheRun),
                NullLogger<ReservationRecalculationHostedService>.Instance);
        }

        public HeldRules Rules { get; } = new();

        public HeldProgrammes Programmes { get; } = new();

        public HeldOutcomes Outcomes { get; }

        public HeldReservations Reservations { get; }

        public HeldStreamVisits Visits { get; } = new();

        public CountedStreams Streams { get; }

        public CountedServices Services { get; } = new();

        public WatchedWrite Write { get; }

        public GatedSeating Seating { get; }

        public TuningByService Tuning { get; }

        public ReservationRecalculationHostedService Recalculating { get; }

        public RuleApplyNow Applying { get; private set; } = null!;

        public static World Taking(int broadcasts)
        {
            var world = new World();

            world.Rules.Rules.Add(world.Written("keyword=hill"));

            foreach (int carried in Enumerable.Range(1, broadcasts))
            {
                world.Programmes.Programmes.Add(world.Broadcast(carried, $"hill walk {carried}"));
            }

            world.Wired();

            return world;
        }

        public static World Losing(int reservations)
        {
            var world = new World();
            Rule rule = world.Written("keyword=heather");
            world.Rules.Rules.Add(rule);
            world.Visits.Visits.Add(StreamVisit.Record(
                new NetworkId(Network),
                new TransportStreamId(Carried),
                VisitOutcome.Complete,
                NowForTheRun.AddHours(-1),
                TimeSpan.FromSeconds(30)));

            foreach (int carried in Enumerable.Range(1, reservations))
            {
                Programme programme = world.Broadcast(carried, $"hill walk {carried}");
                world.Programmes.Programmes.Add(programme);
                world.Reservations.Standing(world.Standing(programme, rule.Id));
            }

            world.Wired();

            return world;
        }

        public void Dispose()
        {
            Recalculating.Dispose();
            provider.Dispose();
        }

        private void Wired()
            => Applying = new RuleApplyNow(Recalculating, new RuleApplySettings(), new FixedClock(NowForTheRun));

        private Rule Written(string query)
            => Rule.Draft(
                new RuleId(new Guid("00000001-0000-0000-0000-000000000000")),
                "a rule",
                new RuleQuery(query),
                Priority.Default,
                true,
                Margin.None,
                Margin.None,
                NowForTheRun.AddDays(-30));

        private Programme Broadcast(int carried, string name)
            => Programme.Rehydrate(
                new ProgrammeId(new NetworkId(Network), new ServiceId(Listed), new EventId(carried)),
                new TransportStreamId(Carried),
                NowForTheRun.AddHours(2 + carried),
                NowForTheRun.AddHours(3 + carried),
                name,
                "a summary",
                false,
                NowForTheRun,
                revision: carried);

        private Reservation Standing(Programme programme, RuleId ruleId)
            => Reservation.Rehydrate(
                new ReservationId(new Guid($"{programme.EventId.Value:x8}-0000-0000-0000-00000000000f")),
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
                    NowForTheRun,
                    AudioMode.Undetermined,
                    ProgrammeSnapshot.SoundsUnannounced),
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
                NowForTheRun);
    }
}
