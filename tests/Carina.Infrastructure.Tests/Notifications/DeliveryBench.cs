using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Tests.DataBroadcast;
using Carina.Infrastructure.Tests.Fixtures.Thumbnails;
using Carina.TestSupport;

using Microsoft.AspNetCore.DataProtection;

namespace Carina.Infrastructure.Tests.Notifications;

/// <summary>
/// The deliveries that wait, held in memory in the order the ledger hands them out.
/// </summary>
internal sealed class HeldDeliveries : INotificationDeliveryRepository
{
    public List<NoticeOnItsWay> Rows { get; } = [];

    public int Saved { get; private set; }

    public int FailingReads { get; set; }

    public int FailingSaves { get; set; }

    public Action? BeforeSave { get; set; }

    public Task<NotificationDelivery?> FindAsync(NotificationDeliveryId id, CancellationToken cancellationToken)
        => Task.FromResult(Rows.Select(row => row.Delivery).SingleOrDefault(delivery => delivery.Id.Equals(id)));

    public Task<IReadOnlyList<NotificationDelivery>> ListOfAsync(NoticeId notice, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<NotificationDelivery>>(
            [.. Rows.Select(row => row.Delivery).Where(delivery => delivery.NoticeId.Equals(notice))]);

    public Task<IReadOnlyList<NoticeOnItsWay>> WaitingAsync(CancellationToken cancellationToken)
    {
        if (FailingReads > 0)
        {
            FailingReads--;

            throw new InvalidOperationException("The ledger could not be read.");
        }

        return Task.FromResult<IReadOnlyList<NoticeOnItsWay>>(
            [.. Rows.Where(row => row.Delivery.IsWaiting).OrderBy(row => row.Notice.OccurredAt).ThenBy(row => row.Delivery.QueuedAt)]);
    }

    public Task<IReadOnlyList<NoticeOnItsWay>> LatestOfAsync(
        NotificationDestinationId destination,
        int count,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<NoticeOnItsWay>>(
            [.. Rows.Where(row => row.Delivery.DestinationId.Equals(destination)).OrderByDescending(row => row.Notice.OccurredAt).Take(count)]);

    public Task SaveAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        BeforeSave?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();

        if (FailingSaves > 0)
        {
            FailingSaves--;
            PutBackAsItWasQueued(delivery);

            throw new InvalidOperationException("The ledger could not be written.");
        }

        Saved++;

        return Task.CompletedTask;
    }

    public NotificationDelivery Held(NotificationDeliveryId id) => Rows.Select(row => row.Delivery).Single(delivery => delivery.Id.Equals(id));

    private void PutBackAsItWasQueued(NotificationDelivery delivery)
    {
        int at = Rows.FindIndex(row => row.Delivery.Id.Equals(delivery.Id));

        Rows[at] = Rows[at] with
        {
            Delivery = NotificationDelivery.Queue(delivery.Id, delivery.NoticeId, delivery.DestinationId, delivery.QueuedAt),
        };
    }
}

/// <summary>
/// The destinations, held in memory, counting how often a result was kept.
/// </summary>
internal sealed class HeldDestinationsOfNotices : INotificationDestinationRepository
{
    public List<NotificationDestination> Destinations { get; } = [];

    public List<NotificationDestinationId> ResultsKept { get; } = [];

    public int SavedWhole { get; private set; }

    public Task<NotificationDestination?> FindAsync(NotificationDestinationId id, CancellationToken cancellationToken)
        => Task.FromResult(Destinations.SingleOrDefault(destination => destination.Id.Equals(id)));

    public Task<NotificationDestination?> FindByEndpointHashAsync(string endpointHash, CancellationToken cancellationToken)
        => Task.FromResult(Destinations.SingleOrDefault(destination => destination.EndpointHash == endpointHash));

    public Task<IReadOnlyList<NotificationDestination>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<NotificationDestination>>([.. Destinations]);

    public Task AddAsync(NotificationDestination destination, CancellationToken cancellationToken)
    {
        Destinations.Add(destination);

        return Task.CompletedTask;
    }

    public Task SaveAsync(NotificationDestination destination, CancellationToken cancellationToken)
    {
        SavedWhole++;

        return Task.CompletedTask;
    }

    public Task KeepResultAsync(NotificationDestination destination, CancellationToken cancellationToken)
    {
        ResultsKept.Add(destination.Id);

        return Task.CompletedTask;
    }

    public Task RemoveAsync(NotificationDestination destination, CancellationToken cancellationToken)
    {
        Destinations.Remove(destination);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Everything a pass over the waiting notices needs, with a webhook or a push service that answers as it is told to
/// and a clock turned by hand.
/// </summary>
internal sealed class DeliveryBench : IAsyncDisposable
{
    public static readonly DateTime Began = new(2031, 4, 2, 3, 0, 0, DateTimeKind.Utc);

    private readonly IDataProtectionProvider keys = new EphemeralDataProtectionProvider();

    public DeliveryBench(Func<HeardRequest, CannedAnswer> answer, NotificationDeliverySettings? settings = null)
        : this(request => Task.FromResult(answer(request)), settings)
    {
    }

    public DeliveryBench(Func<HeardRequest, Task<CannedAnswer>> answer, NotificationDeliverySettings? settings = null)
    {
        Settings = settings ?? new NotificationDeliverySettings();
        Rig = new WebhookRig(answer, clock: Deadlines);
        Seal = new WebhookUrlSeal(keys);
        Push = new PushDesk(keys, Clock);
        Dispatch = new NoticeDispatch(Rig.Line, Settings, Clock);
        Round = RoundThatRemembersNothing();
    }

    public NotificationDeliverySettings Settings { get; }

    public HandTurnedClock Clock { get; } = new(new DateTimeOffset(Began));

    public HandTurnedClock Deadlines { get; } = new(new DateTimeOffset(Began));

    public WebhookRig Rig { get; }

    public WebhookUrlSeal Seal { get; }

    public PushDesk Push { get; }

    public HeldDeliveries Deliveries { get; } = new();

    public HeldDestinationsOfNotices Destinations { get; } = new();

    public NoticeDispatch Dispatch { get; }

    public HeardOf<NotificationDeliveryRound> Said { get; } = new();

    public NotificationDeliveryRound Round { get; }

    public UnwrittenDeliveries Unwritten { get; private set; } = new();

    public DateTime Now => Clock.GetUtcNow().UtcDateTime;

    public IReadOnlyList<string> Paths => [.. Rig.Server.Heard.Select(heard => heard.Target)];

    public static WebhookUrl SlackOf(string token) => WebhookUrl.Read($"https://{WebhookRig.SlackHost}/services/T0000/B0000/{token}")!;

    public NotificationDestination Destination(WebhookUrl webhook, WebhookUrlSeal? sealedBy = null)
    {
        NotificationDestination destination = NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName("Somewhere"),
            webhook.Target,
            (sealedBy ?? Seal).Seal(webhook),
            webhook.Tail,
            Began);
        Destinations.Destinations.Add(destination);

        return destination;
    }

    public NotificationDestination Browser(PushSubscription subscription, PushSubscriptionSeal? sealedBy = null)
    {
        NotificationDestination destination = NotificationDestination.Subscribe(
            NotificationDestinationId.New(),
            new NotificationName("A browser"),
            subscription,
            (sealedBy ?? Push.Seal).Seal(subscription),
            Began);
        Destinations.Destinations.Add(destination);

        return destination;
    }

    public NotificationDestination SealedWithKeysNowLost(WebhookUrl webhook)
        => Destination(webhook, new WebhookUrlSeal(new EphemeralDataProtectionProvider()));

    public NotificationDelivery Waiting(
        NotificationDestination destination,
        DateTime? occurredAt = null,
        NoticeKind kind = NoticeKind.RecordingCompleted,
        string title = "録画が終わりました",
        Guid? source = null,
        string? link = null)
    {
        DateTime at = occurredAt ?? Now;
        Notice notice = Notice.Take(
            NoticeId.New(),
            kind,
            source ?? Guid.NewGuid(),
            at,
            new NoticeWords(title, "作り物の番組\n作り物の局", link),
            at);
        NotificationDelivery delivery = NotificationDelivery.Queue(NotificationDeliveryId.New(), notice.Id, destination.Id, at);
        Deliveries.Rows.Add(new NoticeOnItsWay(delivery, notice));

        return delivery;
    }

    public Task<NotificationDeliveries> PassAsync() => Round.SendAsync(CancellationToken.None);

    public NotificationDeliveryRound RoundThatRemembersNothing()
    {
        Unwritten = new UnwrittenDeliveries();

        return new NotificationDeliveryRound(
            Deliveries,
            Destinations,
            new NotificationDeliveryDomainService(),
            Seal,
            Push.Seal,
            Push.Messages,
            Push.Handling,
            Dispatch,
            Unwritten,
            Settings,
            Clock,
            Said);
    }

    public async ValueTask DisposeAsync()
    {
        await Rig.DisposeAsync();
        Push.Dispose();
    }
}
