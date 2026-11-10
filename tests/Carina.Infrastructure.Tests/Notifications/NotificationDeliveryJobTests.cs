using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Tests.DataBroadcast;
using Carina.Infrastructure.Tests.Thumbnails;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Tests.Notifications;

public sealed class NotificationDeliveryJobTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan BeforeFirstRound = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan BetweenRounds = TimeSpan.FromSeconds(5);

    [Fact(DisplayName = "BR-NC-002: the loop first sends twenty seconds after it starts, and every five seconds from then on")]
    public async Task TheLoopSendsAfterTwentySecondsAndEveryFiveFromThenOn()
    {
        await using Loop machine = new();
        NotificationDestination destination = machine.Bench.Destination(WebhookRig.Slack);
        NotificationDelivery first = machine.Bench.Waiting(destination);

        using NotificationDeliveryJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");
        Assert.Equal([BeforeFirstRound], machine.Bench.Clock.Waiting);
        Assert.Equal(NotificationDeliveryState.Queued, first.State);

        machine.Bench.Clock.Turn(BeforeFirstRound);
        await machine.WaitingAgain("the loop never came back from the first pass");
        Assert.Equal([BetweenRounds], machine.Bench.Clock.Waiting);
        Assert.Equal(NotificationDeliveryState.Sent, first.State);

        NotificationDelivery second = machine.Bench.Waiting(destination);
        machine.Bench.Clock.Turn(BetweenRounds);
        await machine.WaitingAgain("the loop never came back from the second pass");
        await job.StopAsync(Cancel);

        Assert.Equal(NotificationDeliveryState.Sent, second.State);
        Assert.Equal(2, machine.Bench.Paths.Count);
    }

    [Fact(DisplayName = "BR-NS-001: a pass that fails is said once as an error, and the next pass sends what waited")]
    public async Task APassThatFailsIsSaidOnceAndTheNextSends()
    {
        await using Loop machine = new();
        NotificationDelivery delivery = machine.Bench.Waiting(machine.Bench.Destination(WebhookRig.Slack));
        machine.Bench.Deliveries.FailingReads = 1;

        using NotificationDeliveryJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");
        machine.Bench.Clock.Turn(BeforeFirstRound);
        await machine.WaitingAgain("the loop never came back from the pass that failed");

        Assert.Equal(NotificationDeliveryState.Queued, delivery.State);
        Assert.Single(Errors(machine.Said));

        machine.Bench.Clock.Turn(BetweenRounds);
        await machine.WaitingAgain("the loop never came back from the pass after the failure");
        await job.StopAsync(Cancel);

        Assert.Equal(NotificationDeliveryState.Sent, delivery.State);
        Assert.Single(Errors(machine.Said));
    }

    [Fact(DisplayName = "BR-NS-001: notices more than fourteen days old are cleared away at the first pass, and then once an hour")]
    public async Task OldNoticesAreClearedAwayAtTheFirstPassAndThenOnceAnHour()
    {
        await using Loop machine = new(new NotificationDeliverySettings { BetweenRounds = TimeSpan.FromMinutes(20) });

        using NotificationDeliveryJob job = machine.Job();
        await job.StartAsync(Cancel);
        await machine.WaitingAgain("the loop never settled into its first wait");
        machine.Bench.Clock.Turn(BeforeFirstRound);
        await machine.WaitingAgain("the loop never came back from the first pass");
        DateTime first = machine.Bench.Now;

        machine.Bench.Clock.Turn(TimeSpan.FromMinutes(20));
        await machine.WaitingAgain("the loop never came back from the second pass");
        machine.Bench.Clock.Turn(TimeSpan.FromMinutes(20));
        await machine.WaitingAgain("the loop never came back from the third pass");
        Assert.Equal([first.AddDays(-14)], machine.Notices.ClearedBefore);

        machine.Bench.Clock.Turn(TimeSpan.FromMinutes(20));
        await machine.WaitingAgain("the loop never came back from the fourth pass");
        await job.StopAsync(Cancel);

        Assert.Equal([first.AddDays(-14), first.AddHours(1).AddDays(-14)], machine.Notices.ClearedBefore);
    }

    private static IEnumerable<string> Errors<T>(HeardOf<T> said)
        => said.Said.Where(line => line.StartsWith($"{LogLevel.Error} ", StringComparison.Ordinal));

    private sealed class Loop(NotificationDeliverySettings? settings = null) : IAsyncDisposable
    {
        public DeliveryBench Bench { get; } = new(_ => CannedAnswer.Text(200, "ok"), settings);

        public HeardOf<NotificationDeliveryJob> Said { get; } = new();

        public NoticesToClear Notices { get; } = new();

        public NotificationDeliveryJob Job()
        {
            ServiceCollection services = new();
            services.AddSingleton<ILogger<NotificationDeliveryRound>>(Bench.Said);
            services.AddScoped<INotificationDeliveryRepository>(_ => Bench.Deliveries);
            services.AddScoped<INotificationDestinationRepository>(_ => Bench.Destinations);
            services.AddScoped<INoticeRepository>(_ => Notices);
            services.AddSingleton(Bench.Settings);
            services.AddSingleton(Bench.Seal);
            services.AddSingleton(Bench.Dispatch);
            services.AddSingleton<TimeProvider>(Bench.Clock);
            services.AddScoped<NotificationDeliveryRound>();

            return new NotificationDeliveryJob(
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                Bench.Settings,
                Bench.Clock,
                Said);
        }

        public Task WaitingAgain(string what) => Eventually.Happens(() => Bench.Clock.Pending is 1, what);

        public ValueTask DisposeAsync() => Bench.DisposeAsync();
    }

    private sealed class NoticesToClear : INoticeRepository
    {
        public List<DateTime> ClearedBefore { get; } = [];

        public Task<Notice?> FindAsync(NoticeId id, CancellationToken cancellationToken) => Task.FromResult<Notice?>(null);

        public Task<IReadOnlySet<NoticeKey>> HeldAmongAsync(IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlySet<NoticeKey>>(new HashSet<NoticeKey>());

        public Task<bool> AddAsync(Notice notice, IReadOnlyList<NotificationDelivery> deliveries, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<int> RemoveBeforeAsync(DateTime before, CancellationToken cancellationToken)
        {
            ClearedBefore.Add(before);

            return Task.FromResult(0);
        }
    }
}
