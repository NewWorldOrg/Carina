using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Tests.DataBroadcast;
using Carina.Infrastructure.Tests.Thumbnails;
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
        Saved++;

        return Task.CompletedTask;
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
/// Everything a pass over the waiting notices needs, with a webhook that answers as it is told to and a clock turned
/// by hand.
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
        Rig = new WebhookRig(answer);
        Seal = new WebhookUrlSeal(keys);
        Dispatch = new NoticeDispatch(Rig.Line, Settings, Clock);
        Round = new NotificationDeliveryRound(Deliveries, Destinations, Seal, Dispatch, Settings, Clock, Said);
    }

    public NotificationDeliverySettings Settings { get; }

    public HandTurnedClock Clock { get; } = new(new DateTimeOffset(Began));

    public WebhookRig Rig { get; }

    public WebhookUrlSeal Seal { get; }

    public HeldDeliveries Deliveries { get; } = new();

    public HeldDestinationsOfNotices Destinations { get; } = new();

    public NoticeDispatch Dispatch { get; }

    public HeardOf<NotificationDeliveryRound> Said { get; } = new();

    public NotificationDeliveryRound Round { get; }

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

    public NotificationDestination SealedWithKeysNowLost(WebhookUrl webhook)
        => Destination(webhook, new WebhookUrlSeal(new EphemeralDataProtectionProvider()));

    public NotificationDelivery Waiting(
        NotificationDestination destination,
        DateTime? occurredAt = null,
        NoticeKind kind = NoticeKind.RecordingCompleted,
        string title = "録画が終わりました")
    {
        DateTime at = occurredAt ?? Now;
        Notice notice = Notice.Take(
            NoticeId.New(),
            kind,
            Guid.NewGuid(),
            at,
            new NoticeWords(title, "作り物の番組\n作り物の局", Link: null),
            at);
        NotificationDelivery delivery = NotificationDelivery.Queue(NotificationDeliveryId.New(), notice.Id, destination.Id, at);
        Deliveries.Rows.Add(new NoticeOnItsWay(delivery, notice));

        return delivery;
    }

    public Task<NotificationDeliveries> PassAsync() => Round.SendAsync(CancellationToken.None);

    public async ValueTask DisposeAsync() => await Rig.DisposeAsync();
}
