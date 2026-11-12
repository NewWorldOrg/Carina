using Carina.Domain.Notifications;
using Carina.Infrastructure.Tests.Notifications;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Notifications;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class NotificationDeliveryRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Epoch = new(2031, 5, 6, 9, 0, 0, DateTimeKind.Utc);

    private readonly NoticeLedgers ledgers = new(database);

    private readonly ServiceProvider services = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await services.DisposeAsync();

    [Fact(DisplayName = "BR-NS-001: the rows that wait are the queued and the failed, in the order their notices occurred")]
    public async Task TheRowsThatWaitAreTheQueuedAndTheFailedInTheOrderTheirNoticesOccurred()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDestinationId destination = await ledgers.DestinationAsync("Somewhere", Epoch);
        NotificationDeliveryId later = await HeldAsync(destination, Epoch.AddMinutes(3));
        NotificationDeliveryId failed = await HeldAsync(
            destination,
            Epoch.AddMinutes(1),
            delivery => delivery.Fail(NoticeFailure.ServerFailed, Epoch.AddMinutes(2)));
        await HeldAsync(destination, Epoch.AddMinutes(2), delivery => delivery.Deliver(Epoch.AddMinutes(2)));
        await HeldAsync(destination, Epoch, delivery => delivery.GiveUp(NoticeFailure.TooOld));

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IReadOnlyList<NoticeOnItsWay> waiting = await Deliveries(scope).WaitingAsync(Cancel);

        Assert.Equal([failed, later], waiting.Select(row => row.Delivery.Id));
        Assert.Equal([Epoch.AddMinutes(1), Epoch.AddMinutes(3)], waiting.Select(row => row.Notice.OccurredAt));
    }

    [Fact(DisplayName = "FR-005: the latest rows of one destination come newest first and no more than asked")]
    public async Task TheLatestRowsOfOneDestinationComeNewestFirstAndNoMoreThanAsked()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDestinationId asked = await ledgers.DestinationAsync("Asked", Epoch);
        NotificationDestinationId other = await ledgers.DestinationAsync("Other", Epoch);
        await HeldAsync(asked, Epoch);
        NotificationDeliveryId second = await HeldAsync(asked, Epoch.AddMinutes(1), delivery => delivery.Deliver(Epoch.AddMinutes(1)));
        NotificationDeliveryId third = await HeldAsync(asked, Epoch.AddMinutes(2), delivery => delivery.GiveUp(NoticeFailure.Rejected));
        await HeldAsync(other, Epoch.AddMinutes(3));

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IReadOnlyList<NoticeOnItsWay> latest = await Deliveries(scope).LatestOfAsync(asked, 2, Cancel);

        Assert.Equal([third, second], latest.Select(row => row.Delivery.Id));
        Assert.Equal(
            [NotificationDeliveryState.GivenUp, NotificationDeliveryState.Sent],
            latest.Select(row => row.Delivery.State));
        Assert.Equal(NoticeFailure.Rejected, latest[0].Delivery.LastFailure);
    }

    private async Task<NotificationDeliveryId> HeldAsync(
        NotificationDestinationId destination,
        DateTime occurredAt,
        Action<NotificationDelivery>? turn = null)
    {
        Notice notice = Notice.Take(
            NoticeId.New(),
            NoticeKind.RecordingCompleted,
            Guid.NewGuid(),
            occurredAt,
            new NoticeWords("録画が終わりました", "作り物の番組\n作り物の局", Link: null),
            occurredAt);
        NotificationDelivery delivery = NotificationDelivery.Queue(NotificationDeliveryId.New(), notice.Id, destination, occurredAt);
        turn?.Invoke(delivery);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INoticeRepository>().AddAsync(notice, [delivery], Cancel);

        return delivery.Id;
    }

    private static INotificationDeliveryRepository Deliveries(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<INotificationDeliveryRepository>();
}
