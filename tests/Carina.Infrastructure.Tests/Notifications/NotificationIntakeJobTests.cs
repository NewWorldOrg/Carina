using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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

    private sealed class Loop
    {
        public HandTurnedClock Clock { get; } = new(new DateTimeOffset(Began));

        public HeldSources Sources { get; } = new();

        public HeldNotices Notices { get; } = new();

        public HeldRules Rules { get; } = new();

        public HeldDestinations Destinations { get; } = new();

        public NotificationIntakeJob Job()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddScoped<INoticeSourceReader>(_ => Sources);
            services.AddScoped<INoticeRepository>(_ => Notices);
            services.AddScoped<INotificationRuleRepository>(_ => Rules);
            services.AddScoped<INotificationDestinationRepository>(_ => Destinations);
            services.AddSingleton(Settings());
            services.AddSingleton(NoticeLinkBase.None);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddScoped<NotificationIntakeRound>();

            return new NotificationIntakeJob(
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                Settings(),
                Clock,
                NullLogger<NotificationIntakeJob>.Instance);
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
    }

    private sealed class HeldRules : INotificationRuleRepository
    {
        public List<NotificationRule> Rules { get; } = [];

        public Task<NotificationRule?> FindAsync(NotificationRuleId id, CancellationToken cancellationToken)
            => Task.FromResult(Rules.SingleOrDefault(rule => rule.Id.Equals(id)));

        public Task<IReadOnlyList<NotificationRule>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<NotificationRule>>([.. Rules]);

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

        public Task RemoveAsync(NotificationDestination destination, CancellationToken cancellationToken)
        {
            Destinations.Remove(destination);

            return Task.CompletedTask;
        }
    }
}
