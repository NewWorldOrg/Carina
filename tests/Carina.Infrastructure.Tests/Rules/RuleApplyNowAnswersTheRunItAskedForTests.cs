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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Rules;

public sealed class RuleApplyNowAnswersTheRunItAskedForTests
{
    private static readonly DateTime Now = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan LongEnoughToTellAStallFromAWait = TimeSpan.FromSeconds(30);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task TheCountsAnsweredAreTheOnesFromThePassThatMadeTheReservations()
    {
        using World world = World.Taking(3);

        RuleApplyOutcome outcome = await Within(world.Applying.StartAsync(Cancel));

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
        await Within(world.Recalculating.RunAsync(Cancel));

        RuleApplyOutcome outcome = await Within(world.Applying.StartAsync(Cancel));

        Assert.Equal(3, world.Reservations.Held.Count);
        Assert.NotNull(outcome.Run?.Pass.Applied);
        Assert.Equal(3, outcome.Run.Pass.Applied.Made.Count);
    }

    [Fact]
    public async Task WhatIsAnsweredAsMadeIsWhatTheLedgerGained()
    {
        using World world = World.Taking(3);
        int before = world.Reservations.Held.Count;

        RuleApplyOutcome outcome = await Within(world.Applying.StartAsync(Cancel));

        Assert.NotNull(outcome.Run?.Pass.Applied);
        Assert.Equal(world.Reservations.Held.Count - before, outcome.Run.Pass.Applied.Made.Count);
        Assert.True(outcome.Run.Pass.Applied.Made.Count > 0, "the pass made nothing, so nothing was measured");
    }

    [Fact]
    public async Task WhatIsAnsweredAsWithdrawnIsWhatTheLedgerLost()
    {
        using World world = World.Losing(2);
        int before = world.Reservations.Held.Count;

        RuleApplyOutcome outcome = await Within(world.Applying.StartAsync(Cancel));

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

        await world.Seating.Arrived.Task.WaitAsync(LongEnoughToTellAStallFromAWait);

        RuleApplyOutcome outcome = await Within(world.Applying.StartAsync(Cancel));

        world.Seating.Hold.SetResult();
        await walking.WaitAsync(LongEnoughToTellAStallFromAWait);

        Assert.Null(outcome.Run);
        Assert.NotNull(outcome.Refusal);
        Assert.Equal(RuleApplyRefusal.ARecalculationIsAlreadyRunning, outcome.Refusal.Refusal);
    }

    private static Task<T> Within<T>(Task<T> asked) => asked.WaitAsync(LongEnoughToTellAStallFromAWait);

    private sealed class World : IDisposable
    {
        private const int Network = 4;

        private const int Carried = 32_736;

        private const int Listed = 1049;

        private readonly ServiceProvider provider;

        private World()
        {
            Write = new WatchedWrite();
            Reservations = new HeldReservations(Write);
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
            services.AddSingleton<TimeProvider>(new FixedClock(Now));
            services.AddSingleton<IRuleRepository>(Rules);
            services.AddSingleton<IProgrammeRepository>(Programmes);
            services.AddSingleton<IReservationRepository>(Reservations);
            services.AddSingleton<IStreamVisitRepository>(Visits);
            services.AddSingleton<IBroadcastStreamDirectory>(Streams);
            services.AddSingleton<IBroadcastServiceRepository>(Services);
            services.AddSingleton<ITunerCapacityDirectory>(Seating);
            services.AddSingleton<IServiceTuningDirectory>(Tuning);
            services.AddSingleton<IAtomicWrite>(Write);
            services.AddSingleton(RollingHorizon.Default);
            services.AddSingleton(new RuleApplicationSettings());
            services.AddScoped<ProgrammeSearchScope>();
            services.AddScoped<RuleMatcher>();
            services.AddScoped<ReservationSchedulingService>();
            services.AddScoped<RuleApplicationService>();

            provider = services.BuildServiceProvider();

            Recalculating = new ReservationRecalculationHostedService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new RecalculationSettings
                {
                    BeforeFirstPass = TimeSpan.FromHours(1),
                    BetweenReconciliations = TimeSpan.FromHours(1),
                },
                new FixedClock(Now),
                NullLogger<ReservationRecalculationHostedService>.Instance);
        }

        public HeldRules Rules { get; } = new();

        public HeldProgrammes Programmes { get; } = new();

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
                Now.AddHours(-1),
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
            => Applying = new RuleApplyNow(Recalculating, new RuleApplySettings(), new FixedClock(Now));

        private Rule Written(string query)
            => Rule.Draft(
                new RuleId(new Guid("00000001-0000-0000-0000-000000000000")),
                "a rule",
                new RuleQuery(query),
                Priority.Default,
                true,
                Margin.None,
                Margin.None,
                Now.AddDays(-30));

        private Programme Broadcast(int carried, string name)
            => Programme.Rehydrate(
                new ProgrammeId(new NetworkId(Network), new ServiceId(Listed), new EventId(carried)),
                new TransportStreamId(Carried),
                Now.AddHours(2 + carried),
                Now.AddHours(3 + carried),
                name,
                "a summary",
                false,
                Now,
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
    }
}
