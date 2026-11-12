using Carina.Domain.Notifications;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Notifications;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Notifications;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class NoticeRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Epoch = new(2031, 5, 6, 9, 0, 0, DateTimeKind.Utc);

    private readonly NoticeLedgers ledgers = new(database);

    private readonly ServiceProvider services = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await services.DisposeAsync();

    [Fact(DisplayName = "D-4: clearing removes the notices older than asked together with their deliveries")]
    public async Task ClearingRemovesTheNoticesOlderThanAskedTogetherWithTheirDeliveries()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDestinationId destination = await ledgers.DestinationAsync("Somewhere", Epoch);
        DateTime edge = Epoch.AddDays(-14);
        NotificationDeliveryId old = await HeldAsync(destination, edge.AddSeconds(-1));
        NotificationDeliveryId onTheEdge = await HeldAsync(destination, edge);
        NotificationDeliveryId recent = await HeldAsync(destination, Epoch.AddMinutes(-1));

        int removed;
        await using (AsyncServiceScope clearing = services.CreateAsyncScope())
        {
            removed = await Notices(clearing).RemoveBeforeAsync(edge, Cancel);
        }

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        INotificationDeliveryRepository deliveries = scope.ServiceProvider.GetRequiredService<INotificationDeliveryRepository>();
        await using CarinaDbContext context = database.Open();

        Assert.Equal(1, removed);
        Assert.Null(await deliveries.FindAsync(old, Cancel));
        Assert.NotNull(await deliveries.FindAsync(onTheEdge, Cancel));
        Assert.NotNull(await deliveries.FindAsync(recent, Cancel));
        Assert.Equal(2, await context.Set<Notice>().CountAsync(Cancel));
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
        await Notices(scope).AddAsync(notice, [delivery], Cancel);

        return delivery.Id;
    }

    private static INoticeRepository Notices(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<INoticeRepository>();
}
