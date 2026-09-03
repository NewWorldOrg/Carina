using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Programmes;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Rules;
using Carina.Infrastructure.Tests.Rules;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Reservations;

public sealed class ReservationRecalculationHostedServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const int Network = 4;

    private const int Carried = 32_736;

    private const int Listed = 1049;

    [Fact]
    public async Task TheFirstPassAfterTheAppStartsReadsEveryRuleAgainstTheWholeGuide()
    {
        using World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(1, "hill walking"));

        await world.Starting();

        await Eventually.Happens(
            () => world.Reservations.Held.Count is 1,
            "the sweep the app asks for on start made the reservation the rule takes");

        await world.Stopping();
    }

    [Fact]
    public async Task StartingTheServiceDoesNotWaitForTheSweepItAsksFor()
    {
        using World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(1, "hill walking"));
        world.Seating.Hold = new TaskCompletionSource();

        Task starting = world.Recalculating.StartAsync(Cancel);

        await world.Seating.Arrived.Task.WaitAsync(Eventually.Patience);

        Assert.True(starting.IsCompleted, "starting the service returned before the sweep it asked for finished");
        Assert.Empty(world.Reservations.Held);

        world.Seating.Hold.SetResult();

        await Eventually.Happens(
            () => world.Reservations.Held.Count is 1,
            "the sweep that starting did not wait for still landed");

        await world.Stopping();
    }

    [Fact]
    public async Task TwoPassesAreNeverInTheLedgerAtOnce()
    {
        using World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(1, "hill walking"));
        world.Seating.Hold = new TaskCompletionSource();

        world.Recalculating.Nudge(RecalculationTrigger.AppStarted);
        Task<RecalculationPass> first = world.Passing();

        await world.Seating.Arrived.Task.WaitAsync(Eventually.Patience);

        var refused = new List<RecalculationPass>();

        for (int attempt = 0; attempt < 7; attempt++)
        {
            world.Recalculating.Nudge(RecalculationTrigger.TunerConfigurationChanged);
            refused.Add(await world.Passing());
        }

        world.Seating.Hold.SetResult();

        RecalculationPass ran = await first.WaitAsync(Eventually.Patience);

        Assert.True(ran.Ran);
        Assert.Equal(1, world.Seating.Most);
        Assert.All(refused, pass => Assert.Equal(RecalculationRefusal.OneIsAlreadyRunning, pass.Refusal));
    }

    [Fact]
    public async Task ThePassThatWasRefusedRunsOnceTheOneBeforeItHasFinished()
    {
        using World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(1, "hill walking"));
        world.Seating.Hold = new TaskCompletionSource();

        world.Recalculating.Nudge(RecalculationTrigger.AppStarted);
        Task<RecalculationPass> first = world.Passing();

        await world.Seating.Arrived.Task.WaitAsync(Eventually.Patience);

        world.Recalculating.Nudge(RecalculationTrigger.TunerConfigurationChanged);

        Assert.Equal(RecalculationRefusal.OneIsAlreadyRunning, (await world.Passing()).Refusal);

        world.Seating.Hold.SetResult();
        await first.WaitAsync(Eventually.Patience);

        world.Seating.Hold = null;

        RecalculationPass after = await world.Passing();

        Assert.True(after.Ran);
        Assert.Equal([RecalculationTrigger.TunerConfigurationChanged], after.Answering);
        Assert.Equal(1, world.Seating.Most);
        Assert.True(world.Seating.Entered > 1, "the pass that was turned away later ran on its own");
    }

    [Fact]
    public async Task ATriggerThatChangesNothingAsksForNoPassAtAll()
    {
        using World world = World.Of();

        world.Recalculating.Nudge(RecalculationTrigger.TunerFaulted);
        world.Recalculating.Nudge(RecalculationTrigger.ReservationChanged);

        RecalculationPass pass = await world.Passing();

        Assert.Equal(RecalculationRefusal.NothingAsked, pass.Refusal);
        Assert.Equal(0, world.Seating.Entered);
    }

    [Fact]
    public async Task ATriggerThatChangesTheSeatsAsksForAPassThatSettlesTheAllocation()
    {
        using World world = World.Of();

        world.Recalculating.Nudge(RecalculationTrigger.TunerConfigurationChanged);

        RecalculationPass pass = await world.Passing();

        Assert.True(pass.Ran);
        Assert.Equal(RecalculationReach.Settle, pass.Reach);
        Assert.Null(pass.Applied);
        Assert.NotNull(pass.Settled);
        Assert.Equal(1, world.Seating.Entered);
    }

    [Fact]
    public async Task AnIncrementReadsFromWhereTheSweepBeforeItStopped()
    {
        using World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(1, "hill walking", revision: 7));

        world.Recalculating.Nudge(RecalculationTrigger.AppStarted);
        await world.Passing();

        Assert.Equal([0], world.Programmes.AskedFrom);

        world.Guide(Broadcast(2, "hill running", revision: 9));

        world.Recalculating.Nudge(RecalculationTrigger.ProgrammesChanged);
        RecalculationPass second = await world.Passing();

        Assert.Equal(RecalculationReach.Increment, second.Reach);
        Assert.Equal([0, 7], world.Programmes.AskedFrom);

        world.Recalculating.Nudge(RecalculationTrigger.ProgrammesChanged);
        await world.Passing();

        Assert.Equal([0, 7, 9], world.Programmes.AskedFrom);
    }

    [Fact]
    public async Task AnIncrementWhoseRulesThrewReadsFromTheSamePlaceAgain()
    {
        using World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(1, "hill walking", revision: 7));

        world.Recalculating.Nudge(RecalculationTrigger.AppStarted);
        await world.Passing();

        world.Programmes.Throws = new InvalidOperationException("the guide would not answer");
        world.Recalculating.Nudge(RecalculationTrigger.ProgrammesChanged);
        await world.Passing();

        world.Programmes.Throws = null;
        world.Recalculating.Nudge(RecalculationTrigger.ProgrammesChanged);
        await world.Passing();

        Assert.Equal([0, 7, 7], world.Programmes.AskedFrom);
    }

    [Fact]
    public async Task TheAllocationIsStillSettledWhenReadingTheRulesThrows()
    {
        using World world = World.Of();
        world.Reservations.Standing(Standing(Broadcast(1, "hill walking")));
        world.Programmes.Throws = new InvalidOperationException("the guide would not answer");

        world.Recalculating.Nudge(RecalculationTrigger.AppStarted);
        RecalculationPass pass = await world.Passing();

        Assert.Equal([RecalculationStage.Rules], [.. pass.Faults.Select(fault => fault.Stage)]);
        Assert.NotNull(pass.Recorded);
        Assert.Null(pass.Applied);
        Assert.NotNull(pass.Settled);
        Assert.True(pass.Settled.Settled);
        Assert.Equal(1, world.Write.Committed);
    }

    [Fact]
    public async Task TheRulesAreStillReadWhenSettlingTheAllocationThrows()
    {
        using World world = World.Of(seatingThrows: true);

        world.Recalculating.Nudge(RecalculationTrigger.AppStarted);
        RecalculationPass pass = await world.Passing();

        Assert.Equal([RecalculationStage.Scheduling], [.. pass.Faults.Select(fault => fault.Stage)]);
        Assert.NotNull(pass.Recorded);
        Assert.NotNull(pass.Applied);
        Assert.Null(pass.Settled);
    }

    [Fact]
    public async Task APassThatFaultedDoesNotStopTheOneAfterIt()
    {
        using World world = World.Of();
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(1, "hill walking"));
        world.Programmes.Throws = new InvalidOperationException("the guide would not answer");

        world.Recalculating.Nudge(RecalculationTrigger.AppStarted);

        Assert.NotEmpty((await world.Passing()).Faults);

        world.Programmes.Throws = null;
        world.Recalculating.Nudge(RecalculationTrigger.AppStarted);

        RecalculationPass after = await world.Passing();

        Assert.Empty(after.Faults);
        Assert.Single(world.Reservations.Held);
    }

    [Fact]
    public async Task AProgrammeThatVanishedTakesItsReservationWithItWhenTheWholeGuideIsSwept()
    {
        using World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited();
        world.Reservations.Standing(Standing(Broadcast(1, "hill walking"), rule.Id));

        world.Recalculating.Nudge(RecalculationTrigger.AppStarted);
        RecalculationPass pass = await world.Passing();

        Assert.Equal(RecalculationReach.Everything, pass.Reach);
        Assert.Single(pass.Applied!.Withdrawn);
        Assert.Empty(world.Reservations.Held);
    }

    [Fact]
    public async Task AProgrammeThatVanishedKeepsItsReservationWhenOnlyWhatArrivedIsRead()
    {
        using World world = World.Of();
        Rule rule = Written("keyword=hill");
        world.Rules.Rules.Add(rule);
        world.Visited();
        world.Reservations.Standing(Standing(Broadcast(1, "hill walking"), rule.Id));

        world.Recalculating.Nudge(RecalculationTrigger.ProgrammesChanged);
        RecalculationPass pass = await world.Passing();

        Assert.Equal(RecalculationReach.Increment, pass.Reach);
        Assert.Empty(pass.Applied!.Withdrawn);
        Assert.Single(world.Reservations.Held);
    }

    [Fact]
    public async Task TheLoopAnswersANudgeLongBeforeTheWaitBetweenReconciliationsIsUp()
    {
        using World world = World.Of();

        await world.Starting();

        await Eventually.Happens(
            () => world.Seating.Entered is 1,
            "the sweep the app asks for on start settled the allocation once");

        world.Recalculating.Nudge(RecalculationTrigger.TunerConfigurationChanged);

        await Eventually.Happens(
            () => world.Seating.Entered is 2,
            "the loop was woken by the nudge, an hour before the wait it was sitting on would have come due");

        Assert.Equal([0], world.Programmes.AskedFrom);

        await world.Stopping();
    }

    [Fact]
    public async Task TheLoopAsksForAReconciliationNobodyNudgedItFor()
    {
        using World world = World.Of(rushed: true);
        world.Rules.Rules.Add(Written("keyword=hill"));
        world.Guide(Broadcast(1, "hill walking"));

        await world.Starting();

        await Eventually.Happens(
            () => world.Programmes.AskedFrom.Count >= 2,
            "the loop never read the guide again on a wait of its own");

        await world.Stopping();
    }

    [Fact]
    public async Task APassAskedForWithATriggerAnswersForThatTriggerAndNotForNothing()
    {
        using World world = World.Of();

        RecalculationPass pass = await world.Passing(RecalculationTrigger.RulesChanged);

        Assert.True(pass.Ran);
        Assert.Equal([RecalculationTrigger.RulesChanged], pass.Answering);
        Assert.Equal(RecalculationReach.Everything, pass.Reach);
        Assert.NotNull(pass.Applied);
    }

    [Fact]
    public async Task APassAskedForWithATriggerAnswersForItEvenWhenSomebodyElseJustEmptiedTheAsking()
    {
        using World world = World.Of();

        world.Recalculating.Nudge(RecalculationTrigger.SelectedChannelChanged);

        Assert.Equal(RecalculationReach.Settle, (await world.Passing()).Reach);

        RecalculationPass pass = await world.Passing(RecalculationTrigger.RulesChanged);

        Assert.True(pass.Ran);
        Assert.Equal([RecalculationTrigger.RulesChanged], pass.Answering);
        Assert.NotNull(pass.Applied);
    }

    [Fact]
    public async Task APassAskedForWithATriggerCarriesWhatWasAlreadyOnTheBooksAsWell()
    {
        using World world = World.Of();

        world.Recalculating.Nudge(RecalculationTrigger.SelectedChannelChanged);

        RecalculationPass pass = await world.Passing(RecalculationTrigger.RulesChanged);

        Assert.Equal(
            [RecalculationTrigger.RulesChanged, RecalculationTrigger.SelectedChannelChanged],
            [.. pass.Answering.Order()]);
    }

    [Fact]
    public async Task ATriggerTurnedAwayBecauseAPassIsWalkingIsAnsweredByTheNextPass()
    {
        using World world = World.Of();
        world.Seating.Hold = new TaskCompletionSource();

        world.Recalculating.Nudge(RecalculationTrigger.SelectedChannelChanged);
        Task<RecalculationPass> walking = world.Passing();

        await world.Seating.Arrived.Task.WaitAsync(Eventually.Patience);

        RecalculationPass refused = await world.Passing(RecalculationTrigger.RulesChanged);

        world.Seating.Hold.SetResult();
        await walking;
        world.Seating.Hold = null;

        RecalculationPass after = await world.Passing();

        Assert.Equal(RecalculationRefusal.OneIsAlreadyRunning, refused.Refusal);
        Assert.Equal([RecalculationTrigger.RulesChanged], after.Answering);
        Assert.NotNull(after.Applied);
    }

    private static Rule Written(string query, int priority = 10, int identifier = 1)
        => Rule.Draft(
            new RuleId(new Guid($"{identifier:x8}-0000-0000-0000-000000000000")),
            "a rule",
            new RuleQuery(query),
            new Priority(priority),
            enabled: true,
            Margin.None,
            Margin.None,
            Now.AddDays(-30));

    [Fact]
    public async Task AReservationNothingRecordedIsWrittenDownOnceItsWindowHasClosed()
    {
        using World world = World.Of();
        Reservation gone = Passed(Now.AddHours(-2), Now.AddMinutes(-30));
        Reservation running = Passed(Now.AddHours(-1), Now.AddHours(1));
        world.Reservations.Standing(gone, running);

        world.Recalculating.Nudge(RecalculationTrigger.AppStarted);
        RecalculationPass pass = await world.Passing();

        Assert.Empty(pass.Faults);
        Assert.NotNull(pass.Recorded);
        Assert.Equal(
            [new ReservationOutcomeRecord(gone.Id, ReservationOutcomeKind.Missed)],
            pass.Recorded.Recorded);
        Assert.Equal(ReservationState.Missed, gone.State);
        Assert.Equal(ReservationState.Scheduled, running.State);
        Assert.Equal(ReservationOutcomeKind.Missed, Assert.Single(world.Outcomes.Held).Kind);
    }

    [Fact]
    public async Task TheSeatAReservationNobodyRecordedWasHoldingIsFreedInTheSamePass()
    {
        using World world = World.Of();
        Reservation first = Passed(Now.AddHours(-2), Now.AddMinutes(-30), 2001, 21);
        Reservation second = Passed(Now.AddHours(-2), Now.AddMinutes(-30), 2002, 22);
        Reservation running = Passed(Now.AddHours(-1), Now.AddHours(1), 2003, 23);
        world.Tuning.Answer(2001, TuningParameters.Terrestrial(21));
        world.Tuning.Answer(2002, TuningParameters.Terrestrial(22));
        world.Tuning.Answer(2003, TuningParameters.Terrestrial(23));
        world.Reservations.Standing(first, second, running);

        world.Recalculating.Nudge(RecalculationTrigger.AppStarted);
        RecalculationPass pass = await world.Passing();

        Assert.NotNull(pass.Settled);
        Assert.True(pass.Settled.Settled);
        Assert.Equal(ReservationState.Missed, first.State);
        Assert.Equal(ReservationState.Missed, second.State);
        Assert.Equal(ReservationState.Scheduled, running.State);
    }

    [Fact]
    public async Task TheAllocationIsStillSettledWhenWritingDownWhatBecameOfAReservationThrows()
    {
        using World world = World.Of();
        world.Reservations.Standing(Passed(Now.AddHours(-2), Now.AddMinutes(-30)));
        world.Outcomes.Throws = new InvalidOperationException("the ledger would not take the row");

        world.Recalculating.Nudge(RecalculationTrigger.AppStarted);
        RecalculationPass pass = await world.Passing();

        Assert.Equal([RecalculationStage.Outcomes], [.. pass.Faults.Select(fault => fault.Stage)]);
        Assert.Null(pass.Recorded);
        Assert.NotNull(pass.Settled);
        Assert.True(pass.Settled.Settled);
    }

    private static Reservation Passed(DateTime opens, DateTime closes, int service = Listed, int channel = 0)
        => Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(
                new NetworkId(Network),
                new ServiceId(service),
                new EventId(channel is 0 ? Guid.NewGuid().GetHashCode() & 0xFFFF : channel),
                opens),
            null,
            Priority.Default,
            opens,
            closes,
            true,
            Margin.None,
            Margin.None,
            new ProgrammeSnapshot("A programme", "a summary", string.Empty, [], Now),
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

    private static Programme Broadcast(int carried, string name, long revision = 1)
        => Programme.Rehydrate(
            new ProgrammeId(new NetworkId(Network), new ServiceId(Listed), new EventId(carried)),
            new TransportStreamId(Carried),
            Now.AddHours(2),
            Now.AddHours(3),
            name,
            "a summary",
            false,
            Now,
            revision: revision);

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

    private sealed class World : IDisposable
    {
        private readonly ServiceProvider provider;

        private World(bool seatingThrows, bool rushed)
        {
            Write = new WatchedWrite();
            Outcomes = new HeldOutcomes(Write);
            Reservations = new HeldReservations(Write, Outcomes);
            Programmes = new WatchedProgrammes();
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
                        new TunerSeat("first", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                        new TunerSeat("second", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                    ],
                    []),
                seatingThrows);
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
            services.AddSingleton<IReservationOutcomeRepository>(Outcomes);
            services.AddSingleton<IStreamVisitRepository>(Visits);
            services.AddSingleton<IBroadcastStreamDirectory>(Streams);
            services.AddSingleton<IBroadcastServiceRepository>(Services);
            services.AddSingleton<ITunerCapacityDirectory>(Seating);
            services.AddSingleton<IServiceTuningDirectory>(Tuning);
            services.AddSingleton<IAtomicWrite>(Write);
            services.AddSingleton<IReservationRecordingContract>(new HeldClaims());
            services.AddSingleton(RollingHorizon.Default);
            services.AddSingleton(new RuleApplicationSettings());
            services.AddSingleton(new ReservationOutcomeSettings());
            services.AddScoped<ProgrammeSearchScope>();
            services.AddScoped<RuleMatcher>();
            services.AddScoped<ReservationSchedulingService>();
            services.AddScoped<ReservationOutcomeService>();
            services.AddScoped<RuleApplicationService>();

            provider = services.BuildServiceProvider();

            Recalculating = new ReservationRecalculationHostedService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new RecalculationSettings
                {
                    BeforeFirstPass = TimeSpan.FromMilliseconds(1),
                    BetweenReconciliations = TimeSpan.FromHours(1),
                },
                rushed ? new RushedClock(Now) : new FixedClock(Now),
                NullLogger<ReservationRecalculationHostedService>.Instance);
        }

        public HeldRules Rules { get; } = new();

        public WatchedProgrammes Programmes { get; }

        public HeldReservations Reservations { get; }

        public HeldOutcomes Outcomes { get; }

        public HeldStreamVisits Visits { get; } = new();

        public CountedStreams Streams { get; }

        public CountedServices Services { get; } = new();

        public WatchedWrite Write { get; }

        public GatedSeating Seating { get; }

        public TuningByService Tuning { get; }

        public ReservationRecalculationHostedService Recalculating { get; }

        public static World Of(bool seatingThrows = false, bool rushed = false)
            => new(seatingThrows, rushed);

        public Task<RecalculationPass> Passing()
            => Recalculating.RunAsync(CancellationToken.None).WaitAsync(Eventually.Patience);

        public Task<RecalculationPass> Passing(RecalculationTrigger asking)
            => Recalculating.RunAsync(asking, CancellationToken.None).WaitAsync(Eventually.Patience);

        public Task Starting()
            => Recalculating.StartAsync(CancellationToken.None).WaitAsync(Eventually.Patience);

        public Task Stopping()
            => Recalculating.StopAsync(CancellationToken.None).WaitAsync(Eventually.Patience);

        public void Guide(params Programme[] programmes) => Programmes.Held.AddRange(programmes);

        public void Visited()
            => Visits.Visits.Add(StreamVisit.Record(
                new NetworkId(Network),
                new TransportStreamId(Carried),
                VisitOutcome.Complete,
                Now.AddHours(-1),
                TimeSpan.FromSeconds(30)));

        public void Dispose()
        {
            Recalculating.Dispose();
            provider.Dispose();
        }
    }
}
