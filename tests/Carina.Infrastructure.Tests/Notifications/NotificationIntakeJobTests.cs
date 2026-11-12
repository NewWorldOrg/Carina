using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Tests.Thumbnails;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using EventId = Carina.Domain.Programmes.EventId;

namespace Carina.Infrastructure.Tests.Notifications;

public sealed class NotificationIntakeJobTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Began = new(2031, 4, 2, 3, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan BeforeFirstLook = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan BetweenLooks = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan LooksBack = TimeSpan.FromMinutes(30);

    [Fact(DisplayName = "BR-NC-004: the loop looks every fifteen seconds, thirty minutes back each time")]
    public async Task TheLoopLooksEveryFifteenSecondsThirtyMinutesBackEachTime()
    {
        Loop machine = new();

        using NotificationIntakeJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");
        machine.Clock.Turn(BeforeFirstLook);
        await machine.WaitingAgain("the loop never came back from the first look");
        machine.Clock.Turn(BetweenLooks);
        await machine.WaitingAgain("the loop never came back from the second look");
        await job.StopAsync(Cancel);

        Assert.Equal(
            [Began + BeforeFirstLook - LooksBack, Began + BeforeFirstLook + BetweenLooks - LooksBack],
            machine.Sources.Asked);
    }

    [Fact(DisplayName = "BR-NC-004: what happened exactly thirty minutes before a look is taken, and what happened a tick before that is not")]
    public async Task WhatHappenedExactlyThirtyMinutesBeforeALookIsTakenAndATickBeforeIsNot()
    {
        Loop machine = new();
        DateTime edge = Began + BeforeFirstLook - LooksBack;
        Guid onTheEdge = machine.RecordingBegan(edge);
        machine.RecordingBegan(edge - TimeSpan.FromTicks(1));

        using NotificationIntakeJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");
        machine.Clock.Turn(BeforeFirstLook);
        await machine.WaitingAgain("the loop never came back from the look");
        await job.StopAsync(Cancel);

        Notice notice = Assert.Single(machine.Notices.Held);
        Assert.Equal(onTheEdge, notice.SourceId);
    }

    [Fact(DisplayName = "BR-ND-004: what one look took is not taken again at the next, and what came since is")]
    public async Task WhatOneLookTookIsNotTakenAgainAtTheNextAndWhatCameSinceIs()
    {
        Loop machine = new();
        Guid first = machine.RecordingBegan(Began.AddSeconds(5));

        using NotificationIntakeJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");
        machine.Clock.Turn(BeforeFirstLook);
        await machine.WaitingAgain("the loop never came back from the first look");
        Guid second = machine.RecordingBegan(Began.AddSeconds(20));
        machine.Clock.Turn(BetweenLooks);
        await machine.WaitingAgain("the loop never came back from the second look");
        await job.StopAsync(Cancel);

        Assert.Equal([first, second], machine.Notices.Held.Select(notice => notice.SourceId));
    }

    [Fact(DisplayName = "a look that fails does not stop the next one")]
    public async Task ALookThatFailsDoesNotStopTheNextOne()
    {
        Loop machine = new();
        Guid recording = machine.RecordingBegan(Began.AddSeconds(5));
        machine.Sources.FailingLooks = 1;

        using NotificationIntakeJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");
        machine.Clock.Turn(BeforeFirstLook);
        await machine.WaitingAgain("the loop never came back from the look that failed");

        Assert.Empty(machine.Notices.Held);

        machine.Clock.Turn(BetweenLooks);
        await machine.WaitingAgain("the loop never came back from the look after the failure");
        await job.StopAsync(Cancel);

        Notice notice = Assert.Single(machine.Notices.Held);
        Assert.Equal(recording, notice.SourceId);
    }

    [Fact(DisplayName = "a look that fails is logged once as an error, and the looks that go well log none")]
    public async Task ALookThatFailsIsLoggedOnceAsAnError()
    {
        Loop machine = new();
        machine.RecordingBegan(Began.AddSeconds(5));
        machine.Sources.FailingLooks = 1;

        using NotificationIntakeJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");
        machine.Clock.Turn(BeforeFirstLook);
        await machine.WaitingAgain("the loop never came back from the look that failed");
        machine.Clock.Turn(BetweenLooks);
        await machine.WaitingAgain("the loop never came back from the look after the failure");
        await job.StopAsync(Cancel);

        Assert.Single(Errors(machine.SaidOfLooks));
        Assert.Empty(Errors(machine.SaidOfNotices));
    }

    [Fact(DisplayName = "a notice that cannot be written is logged once as an error, naming the row it came from")]
    public async Task ANoticeThatCannotBeWrittenIsLoggedOnceAsAnError()
    {
        Loop machine = new();
        Guid refused = machine.RecordingBegan(Began.AddSeconds(5));
        machine.RecordingBegan(Began.AddSeconds(6));
        machine.Notices.Refusing = refused;

        using NotificationIntakeJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");
        machine.Clock.Turn(BeforeFirstLook);
        await machine.WaitingAgain("the loop never came back from the look");
        await job.StopAsync(Cancel);

        string error = Assert.Single(Errors(machine.SaidOfNotices));
        Assert.Contains(refused.ToString(), error, StringComparison.Ordinal);
        Assert.Empty(Errors(machine.SaidOfLooks));
    }

    [Fact(DisplayName = "a notice that cannot be written does not keep the others of the same look from being written")]
    public async Task ANoticeThatCannotBeWrittenDoesNotKeepTheOthersFromBeingWritten()
    {
        Loop machine = new();
        Guid refused = machine.RecordingBegan(Began.AddSeconds(5));
        Guid written = machine.RecordingBegan(Began.AddSeconds(6));
        machine.Notices.Refusing = refused;

        using NotificationIntakeJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");
        machine.Clock.Turn(BeforeFirstLook);
        await machine.WaitingAgain("the loop never came back from the look");
        await job.StopAsync(Cancel);

        Notice notice = Assert.Single(machine.Notices.Held);
        Assert.Equal(written, notice.SourceId);
    }

    [Fact(DisplayName = "BR-ND-010: a notice queues one delivery for each destination the rule sets name for it")]
    public async Task ANoticeQueuesOneDeliveryForEachDestinationTheRuleSetsNameForIt()
    {
        Loop machine = new();
        NotificationDestinationId destination = machine.Destination();
        machine.Rule([NoticeKind.RecordingStarted], destination);
        machine.Rule([NoticeKind.RecordingStarted, NoticeKind.RecordingCompleted], destination);
        machine.RecordingBegan(Began.AddSeconds(5));

        using NotificationIntakeJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");
        machine.Clock.Turn(BeforeFirstLook);
        await machine.WaitingAgain("the loop never came back from the look");
        await job.StopAsync(Cancel);

        NotificationDelivery delivery = Assert.Single(machine.Notices.Queued);
        Assert.Equal(destination, delivery.DestinationId);
        Assert.Equal(Assert.Single(machine.Notices.Held).Id, delivery.NoticeId);
    }

    [Fact(DisplayName = "BR-NC-003: each look also tells of the programmes asked to be told of, to the destinations asked for with them")]
    public async Task EachLookAlsoTellsOfTheProgrammesAskedToBeToldOf()
    {
        Loop machine = new();
        NotificationDestinationId destination = machine.Destination();
        ProgrammeWatchId watch = machine.AskedToBeToldOf(Began + BeforeFirstLook + TimeSpan.FromMinutes(2), destination);

        using NotificationIntakeJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");
        machine.Clock.Turn(BeforeFirstLook);
        await machine.WaitingAgain("the loop never came back from the look");
        await job.StopAsync(Cancel);

        Notice notice = Assert.Single(machine.Notices.Held);
        Assert.Equal(NoticeKind.ProgrammeStarting, notice.Kind);
        Assert.Equal(watch.Value, notice.SourceId);
        Assert.Equal(destination, Assert.Single(machine.Notices.Queued).DestinationId);
        Assert.Equal(ProgrammeWatchState.Notified, Assert.Single(machine.Watches.Watches).State);
    }

    [Fact(DisplayName = "BR-NC-003: a look at the ledgers that fails does not keep the programmes asked to be told of from being told")]
    public async Task ALookAtTheLedgersThatFailsDoesNotKeepTheProgrammesFromBeingTold()
    {
        Loop machine = new();
        ProgrammeWatchId watch = machine.AskedToBeToldOf(Began + BeforeFirstLook + TimeSpan.FromMinutes(2), machine.Destination());
        machine.Sources.FailingLooks = 1;

        using NotificationIntakeJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");
        machine.Clock.Turn(BeforeFirstLook);
        await machine.WaitingAgain("the loop never came back from the look that failed");
        await job.StopAsync(Cancel);

        Assert.Equal(watch.Value, Assert.Single(machine.Notices.Held).SourceId);
        Assert.Single(Errors(machine.SaidOfLooks));
        Assert.Empty(Errors(machine.SaidOfWatches));
    }

    [Fact(DisplayName = "BR-NS-003: a programme that cannot be told of is logged once as an error, naming its row")]
    public async Task AProgrammeThatCannotBeToldOfIsLoggedOnceNamingItsRow()
    {
        Loop machine = new();
        ProgrammeWatchId watch = machine.AskedToBeToldOf(Began + BeforeFirstLook + TimeSpan.FromMinutes(2), machine.Destination());
        machine.Notices.Refusing = watch.Value;

        using NotificationIntakeJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");
        machine.Clock.Turn(BeforeFirstLook);
        await machine.WaitingAgain("the loop never came back from the look");
        await job.StopAsync(Cancel);

        string error = Assert.Single(Errors(machine.SaidOfWatches));
        Assert.Contains(watch.Value.ToString(), error, StringComparison.Ordinal);
        Assert.Empty(Errors(machine.SaidOfLooks));
        Assert.Empty(machine.Notices.Held);
    }

    private static IEnumerable<string> Errors<T>(HeardOf<T> said)
        => said.Said.Where(line => line.StartsWith($"{LogLevel.Error} ", StringComparison.Ordinal));

    private sealed class Loop
    {
        public HeardOf<NotificationIntakeJob> SaidOfLooks { get; } = new();

        public HeardOf<NotificationIntakeRound> SaidOfNotices { get; } = new();

        public HandTurnedClock Clock { get; } = new(new DateTimeOffset(Began));

        public HeldSources Sources { get; } = new();

        public HeldNotices Notices { get; } = new();

        public HeldRules Rules { get; } = new();

        public HeldDestinations Destinations { get; } = new();

        public HeldProgrammeWatches Watches { get; } = new();

        public HeardOf<ProgrammeWatchRound> SaidOfWatches { get; } = new();

        public NotificationIntakeJob Job()
        {
            ServiceCollection services = new();
            services.AddSingleton<ILogger<NotificationIntakeRound>>(SaidOfNotices);
            services.AddScoped<INoticeSourceReader>(_ => Sources);
            services.AddScoped<INoticeRepository>(_ => Notices);
            services.AddScoped<INotificationRuleRepository>(_ => Rules);
            services.AddScoped<INotificationDestinationRepository>(_ => Destinations);
            services.AddSingleton(Settings());
            services.AddSingleton(NoticeLinkBase.None);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddScoped<NotificationIntakeDomainService>();
            services.AddScoped<NotificationIntakeRound>();
            services.AddSingleton<ILogger<ProgrammeWatchRound>>(SaidOfWatches);
            services.AddScoped<IProgrammeWatchRepository>(_ => Watches);
            services.AddScoped<IAnnouncedProgrammes>(_ => new HeldProgrammes());
            services.AddScoped<IBroadcastServiceRepository>(_ => new HeldServices());
            services.AddScoped<IAtomicWrite>(_ => new UnguardedWrites());
            services.AddScoped<ProgrammeWatchRound>();

            return new NotificationIntakeJob(
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                Settings(),
                Clock,
                SaidOfLooks);
        }

        public Task WaitingAgain(string what) => Eventually.Happens(() => Clock.Pending is 1, what);

        public Guid RecordingBegan(DateTime at)
        {
            Guid recording = Guid.NewGuid();
            Sources.Sources.Add(new RecordingNoticeSource(
                recording,
                Reserved: false,
                "A made-up programme",
                Station: null,
                at,
                at.AddMinutes(30),
                at,
                SettledAt: null,
                Outcome: null,
                MissedMs: 0,
                Faults: []));

            return recording;
        }

        public ProgrammeWatchId AskedToBeToldOf(DateTime startsAt, NotificationDestinationId destination)
        {
            ProgrammeWatch watch = ProgrammeWatch.Ask(
                ProgrammeWatchId.New(),
                new ProgrammeId(new NetworkId(32736), new ServiceId(1984), new EventId(7)),
                new WatchedBroadcast(startsAt, startsAt.AddMinutes(30), "A made-up programme", null),
                3,
                [destination],
                Began);
            Watches.Watches.Add(watch);

            return watch.Id;
        }

        public NotificationDestinationId Destination()
        {
            NotificationDestination destination = NotificationDestination.Register(
                NotificationDestinationId.New(),
                new NotificationName("Somewhere"),
                WebhookTarget.Discord,
                [0x01, 0x02, 0x03],
                "0000",
                Began);
            Destinations.Destinations.Add(destination);

            return destination.Id;
        }

        public void Rule(IReadOnlyList<NoticeKind> events, NotificationDestinationId destination)
            => Rules.Rules.Add(NotificationRule.Define(
                NotificationRuleId.New(),
                new NotificationName("A rule set"),
                events,
                [destination],
                Began));

        private static NotificationIntakeSettings Settings()
            => new() { BeforeFirstLook = BeforeFirstLook, BetweenLooks = BetweenLooks, LooksBack = LooksBack };
    }

    private sealed class HeldSources : INoticeSourceReader
    {
        public List<NoticeSource> Sources { get; } = [];

        public List<DateTime> Asked { get; } = [];

        public int FailingLooks { get; set; }

        public Task<IReadOnlyList<NoticeSource>> SinceAsync(DateTime since, CancellationToken cancellationToken)
        {
            Asked.Add(since);

            if (FailingLooks > 0)
            {
                FailingLooks--;

                throw new InvalidOperationException("The ledgers could not be read.");
            }

            return Task.FromResult<IReadOnlyList<NoticeSource>>([.. Sources]);
        }
    }

    private sealed class HeldNotices : INoticeRepository
    {
        public List<Notice> Held { get; } = [];

        public List<NotificationDelivery> Queued { get; } = [];

        public Guid? Refusing { get; set; }

        public Task<Notice?> FindAsync(NoticeId id, CancellationToken cancellationToken)
            => Task.FromResult(Held.SingleOrDefault(notice => notice.Id.Equals(id)));

        public Task<IReadOnlySet<NoticeKey>> HeldAmongAsync(IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<NoticeKey>>(
                Held.Where(notice => sourceIds.Contains(notice.SourceId))
                    .Select(notice => new NoticeKey(notice.Kind, notice.SourceId))
                    .ToHashSet());

        public Task<bool> AddAsync(Notice notice, IReadOnlyList<NotificationDelivery> deliveries, CancellationToken cancellationToken)
        {
            if (notice.SourceId == Refusing)
            {
                throw new InvalidOperationException("The ledger could not be written.");
            }

            if (Held.Any(held => held.Kind == notice.Kind && held.SourceId == notice.SourceId))
            {
                return Task.FromResult(false);
            }

            Held.Add(notice);
            Queued.AddRange(deliveries);

            return Task.FromResult(true);
        }

        public Task<int> RemoveBeforeAsync(DateTime before, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class HeldRules : INotificationRuleRepository
    {
        public List<NotificationRule> Rules { get; } = [];

        public Task<NotificationRule?> FindAsync(NotificationRuleId id, CancellationToken cancellationToken)
            => Task.FromResult(Rules.SingleOrDefault(rule => rule.Id.Equals(id)));

        public Task<IReadOnlyList<NotificationRule>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<NotificationRule>>([.. Rules]);

        public Task TakeTurnAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AddAsync(NotificationRule rule, CancellationToken cancellationToken)
        {
            Rules.Add(rule);

            return Task.CompletedTask;
        }

        public Task SaveAsync(NotificationRule rule, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RemoveAsync(NotificationRule rule, CancellationToken cancellationToken)
        {
            Rules.Remove(rule);

            return Task.CompletedTask;
        }
    }

    private sealed class HeldDestinations : INotificationDestinationRepository
    {
        public List<NotificationDestination> Destinations { get; } = [];

        public Task<NotificationDestination?> FindAsync(NotificationDestinationId id, CancellationToken cancellationToken)
            => Task.FromResult(Destinations.SingleOrDefault(destination => destination.Id.Equals(id)));

        public Task<IReadOnlyList<NotificationDestination>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<NotificationDestination>>([.. Destinations]);

        public Task AddAsync(NotificationDestination destination, CancellationToken cancellationToken)
        {
            Destinations.Add(destination);

            return Task.CompletedTask;
        }

        public Task SaveAsync(NotificationDestination destination, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task KeepResultAsync(NotificationDestination destination, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RemoveAsync(NotificationDestination destination, CancellationToken cancellationToken)
        {
            Destinations.Remove(destination);

            return Task.CompletedTask;
        }
    }
}
