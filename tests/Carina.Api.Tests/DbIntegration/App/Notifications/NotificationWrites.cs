using Carina.Api.Common;
using Carina.Api.Services;
using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using EventId = Carina.Domain.Programmes.EventId;

namespace Carina.Api.Tests.DbIntegration.App.Notifications;

/// <summary>
/// The application raised over the scratch database for the writes of the notifications, with a clock turned by
/// hand, and the rows a test needs written or read beside the service it asks.
/// </summary>
internal sealed class NotificationWrites(AppDatabase database) : IAsyncDisposable
{
    public const string Address = "https://hooks.slack.com/services/T0000/B0000/made-up-token-wxyz";

    public const string Endpoint = "https://fcm.googleapis.com/fcm/send/made-up-registration-qrst";

    public const int AtOnce = 6;

    public static readonly DateTime Noon = new(2031, 6, 7, 12, 0, 0, DateTimeKind.Utc);

    private const string Screens = "https://screens.example";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly Lock gate = new();

    private readonly Dictionary<(bool Rules, bool Watches), RaisedApplication> raised = [];

    public HandTurnedClock Clock { get; } = new(new DateTimeOffset(Noon));

    public static NotificationRuleChange Asked(params NotificationDestinationId[] destinations)
        => new("A made-up set", null, [NoticeKind.RecordingFailed], [.. destinations.Select(destination => destination.Value)]);

    /// <summary>
    /// A scope of the application, in which the sets of rules cannot be added when <paramref name="rulesCanBeAdded"/>
    /// is false and the programmes asked to be told of cannot be saved when <paramref name="watchesCanBeSaved"/> is false.
    /// </summary>
    public AsyncServiceScope Scope(bool rulesCanBeAdded = true, bool watchesCanBeSaved = true)
    {
        lock (gate)
        {
            return Raised(rulesCanBeAdded, watchesCanBeSaved).Services.CreateAsyncScope();
        }
    }

    private RaisedApplication Raised(bool rulesCanBeAdded, bool watchesCanBeSaved)
    {
        if (!raised.TryGetValue((rulesCanBeAdded, watchesCanBeSaved), out RaisedApplication? application))
        {
            application = database.Application(Clock, Screens, services =>
            {
                if (!rulesCanBeAdded)
                {
                    RegisteredServices.Wrap<INotificationRuleRepository>(services, held => new RulesNobodyCanAdd(held));
                }

                if (!watchesCanBeSaved)
                {
                    RegisteredServices.Wrap<IProgrammeWatchRepository>(services, held => new WatchesNobodyCanSave(held));
                }
            });
            raised[(rulesCanBeAdded, watchesCanBeSaved)] = application;
        }

        return application;
    }

    public async Task WaitsItsTurnAsync<T>(Func<Task<T>> write)
    {
        await using AsyncServiceScope first = Scope();
        await using IDbContextTransaction holding = await first.ServiceProvider.GetRequiredService<CarinaDbContext>()
            .Database.BeginTransactionAsync(Cancel);

        await first.ServiceProvider.GetRequiredService<INotificationRuleRepository>().TakeTurnAsync(Cancel);

        Task<T> next = write();

        Assert.NotSame(next, await Task.WhenAny(next, Task.Delay(TimeSpan.FromMilliseconds(300), Cancel)));

        await holding.CommitAsync(Cancel);
        await next.WaitAsync(TimeSpan.FromSeconds(10), Cancel);
    }

    public async Task<NotificationDestinationId> RegisteredAsync(string name)
    {
        await using AsyncServiceScope scope = Scope();
        ServiceResult<ListedNotificationDestination, NotificationFailure> answer =
            await scope.ServiceProvider.GetRequiredService<NotificationDestinationService>().RegisterAsync(name, Address, Cancel);

        return answer.Data!.Destination.Id;
    }

    public async Task<ServiceResult<NotificationRule, NotificationFailure>> DefinedAsync(NotificationRuleChange change)
    {
        await using AsyncServiceScope scope = Scope();

        return await scope.ServiceProvider.GetRequiredService<NotificationRuleService>().DefineAsync(change, Cancel);
    }

    public async Task RulesAreHeldAsync(NotificationDestinationId destination, int count)
    {
        await using AsyncServiceScope scope = Scope();
        INotificationRuleRepository rules = scope.ServiceProvider.GetRequiredService<INotificationRuleRepository>();

        for (int index = 0; index < count; index++)
        {
            await rules.AddAsync(
                NotificationRule.Define(
                    NotificationRuleId.New(),
                    new NotificationName($"A made-up set {index}"),
                    [NoticeKind.RecordingFailed],
                    [destination],
                    Noon),
                Cancel);
        }
    }

    public async Task<ProgrammeWatch> AskedToBeToldAsync(int carried, params NotificationDestinationId[] destinations)
    {
        ProgrammeWatch watch = ProgrammeWatch.Ask(
            ProgrammeWatchId.New(),
            new ProgrammeId(new NetworkId(32_736), new ServiceId(1024), new EventId(carried)),
            new WatchedBroadcast(Noon.AddHours(2), Noon.AddHours(3), "作り物の番組", "作り物の局"),
            3,
            destinations,
            Noon);

        await using AsyncServiceScope scope = Scope();
        await scope.ServiceProvider.GetRequiredService<IProgrammeWatchRepository>().AddAsync(watch, Cancel);

        return watch;
    }

    public async Task<Notice> ToldAsync(params NotificationDestinationId[] destinations)
    {
        Notice notice = Notice.Take(
            NoticeId.New(),
            NoticeKind.RecordingCompleted,
            Guid.NewGuid(),
            Noon,
            new NoticeWords("録画が終わりました", "作り物の番組\n作り物の局", Link: null),
            Noon);

        await using AsyncServiceScope scope = Scope();
        await scope.ServiceProvider.GetRequiredService<INoticeRepository>().AddAsync(
            notice,
            [.. destinations.Select(destination => NotificationDelivery.Queue(NotificationDeliveryId.New(), notice.Id, destination, Noon))],
            Cancel);

        return notice;
    }

    public async Task<IReadOnlyList<NotificationRule>> HeldRulesAsync()
    {
        await using AsyncServiceScope scope = Scope();

        return await scope.ServiceProvider.GetRequiredService<INotificationRuleRepository>().ListAsync(Cancel);
    }

    public async Task<IReadOnlyList<NotificationDestination>> HeldDestinationsAsync()
    {
        await using AsyncServiceScope scope = Scope();

        return await scope.ServiceProvider.GetRequiredService<INotificationDestinationRepository>().ListAsync(Cancel);
    }

    public async Task EmptyAsync()
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlCommand emptying = new(
            "DELETE FROM notification_event; DELETE FROM notification_rule; DELETE FROM notification_destination; "
            + "DELETE FROM notification_watch;",
            connection);
        await emptying.ExecuteNonQueryAsync(Cancel);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (RaisedApplication application in raised.Values)
        {
            await application.DisposeAsync();
        }

        raised.Clear();
    }

    private sealed class RulesNobodyCanAdd(INotificationRuleRepository held) : INotificationRuleRepository
    {
        public Task<NotificationRule?> FindAsync(NotificationRuleId id, CancellationToken cancellationToken)
            => held.FindAsync(id, cancellationToken);

        public Task<IReadOnlyList<NotificationRule>> ListAsync(CancellationToken cancellationToken)
            => held.ListAsync(cancellationToken);

        public Task TakeTurnAsync(CancellationToken cancellationToken) => held.TakeTurnAsync(cancellationToken);

        public Task AddAsync(NotificationRule rule, CancellationToken cancellationToken)
            => throw new InvalidOperationException("This ledger takes no set of rules.");

        public Task SaveAsync(NotificationRule rule, CancellationToken cancellationToken)
            => held.SaveAsync(rule, cancellationToken);

        public Task RemoveAsync(NotificationRule rule, CancellationToken cancellationToken)
            => held.RemoveAsync(rule, cancellationToken);
    }

    private sealed class WatchesNobodyCanSave(IProgrammeWatchRepository held) : IProgrammeWatchRepository
    {
        public Task<ProgrammeWatch?> FindAsync(ProgrammeWatchId id, CancellationToken cancellationToken)
            => held.FindAsync(id, cancellationToken);

        public Task<ProgrammeWatch?> FindOfAsync(ProgrammeId programme, CancellationToken cancellationToken)
            => held.FindOfAsync(programme, cancellationToken);

        public Task<IReadOnlyList<ProgrammeWatch>> ListAsync(CancellationToken cancellationToken)
            => held.ListAsync(cancellationToken);

        public Task<IReadOnlyList<ProgrammeWatch>> ListFollowedAsync(DateTime at, CancellationToken cancellationToken)
            => held.ListFollowedAsync(at, cancellationToken);

        public Task AddAsync(ProgrammeWatch watch, CancellationToken cancellationToken)
            => held.AddAsync(watch, cancellationToken);

        public Task SaveAsync(ProgrammeWatch watch, CancellationToken cancellationToken)
            => throw new InvalidOperationException("This ledger keeps its programmes as they are.");

        public Task RemoveAsync(ProgrammeWatch watch, CancellationToken cancellationToken)
            => held.RemoveAsync(watch, cancellationToken);

        public Task<int> RemoveSettledBeforeAsync(DateTime before, CancellationToken cancellationToken)
            => held.RemoveSettledBeforeAsync(before, cancellationToken);
    }
}
