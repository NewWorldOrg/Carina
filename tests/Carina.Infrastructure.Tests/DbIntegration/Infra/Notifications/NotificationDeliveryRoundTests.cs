using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Fixtures.DataBroadcast;
using Carina.Infrastructure.Tests.Notifications;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Infra.Notifications;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class NotificationDeliveryRoundTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Epoch = new(2031, 5, 6, 9, 0, 0, DateTimeKind.Utc);

    private readonly NoticeLedgers ledgers = new(database);

    [Fact(DisplayName = "BR-NS-001: a pass over the ledger sends what waits and writes that it was sent")]
    public async Task APassOverTheLedgerSendsWhatWaitsAndWritesThatItWasSent()
    {
        await ledgers.EmptyTheNoticesAsync();
        await using WebhookRig rig = new(_ => CannedAnswer.Text(200, "ok"));
        HandTurnedClock clock = new(new DateTimeOffset(Epoch));
        await using ServiceProvider services = Over(rig, clock);
        NotificationDestination destination = await RegisteredAsync(services);

        NotificationDeliveryId first = await HeldAsync(services, destination.Id, Epoch.AddMinutes(-2));
        NotificationDeliveryId second = await HeldAsync(services, destination.Id, Epoch.AddMinutes(-1));

        NotificationDeliveries moved;
        await using (AsyncServiceScope passing = services.CreateAsyncScope())
        {
            moved = await passing.ServiceProvider.GetRequiredService<NotificationDeliveryRound>().SendAsync(Cancel);
        }

        await using AsyncServiceScope reading = services.CreateAsyncScope();
        INotificationDeliveryRepository deliveries = reading.ServiceProvider.GetRequiredService<INotificationDeliveryRepository>();
        NotificationDestination held = (await reading.ServiceProvider.GetRequiredService<INotificationDestinationRepository>()
            .FindAsync(destination.Id, Cancel))!;

        Assert.Equal(new NotificationDeliveries(2, 0, 0), moved);
        Assert.Equal(2, rig.Server.Heard.Count);
        Assert.Equal(NotificationDeliveryState.Sent, (await deliveries.FindAsync(first, Cancel))!.State);
        Assert.Equal(Epoch, (await deliveries.FindAsync(second, Cancel))!.SentAt);
        Assert.Empty(await deliveries.WaitingAsync(Cancel));
        Assert.Equal(Epoch, held.LastResultAt);
        Assert.True(held.Enabled);
    }

    [Fact(DisplayName = "BR-NS-001: a delivery removed while it was being sent stays gone, is not remembered, and does not keep the next one from being written")]
    public async Task ADeliveryRemovedWhileItWasBeingSentStaysGone()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDeliveryId? removedOnTheWay = null;
        await using WebhookRig rig = new(async _ =>
        {
            if (removedOnTheWay is { } leaving)
            {
                removedOnTheWay = null;
                await using CarinaDbContext removing = database.Open();
                await removing.Set<NotificationDelivery>().Where(delivery => delivery.Id == leaving).ExecuteDeleteAsync(Cancel);
            }

            return CannedAnswer.Text(200, "ok");
        });
        HandTurnedClock clock = new(new DateTimeOffset(Epoch));
        await using ServiceProvider services = Over(rig, clock);
        NotificationDestination destination = await RegisteredAsync(services);

        NotificationDeliveryId first = await HeldAsync(services, destination.Id, Epoch.AddMinutes(-2));
        NotificationDeliveryId second = await HeldAsync(services, destination.Id, Epoch.AddMinutes(-1));
        removedOnTheWay = first;
        UnwrittenDeliveries unwritten = services.GetRequiredService<UnwrittenDeliveries>();

        await using (AsyncServiceScope passing = services.CreateAsyncScope())
        {
            await passing.ServiceProvider.GetRequiredService<NotificationDeliveryRound>().SendAsync(Cancel);
        }

        await using AsyncServiceScope reading = services.CreateAsyncScope();
        INotificationDeliveryRepository deliveries = reading.ServiceProvider.GetRequiredService<INotificationDeliveryRepository>();

        Assert.Equal(2, rig.Server.Heard.Count);
        Assert.Null(await deliveries.FindAsync(first, Cancel));
        Assert.Equal(NotificationDeliveryState.Sent, (await deliveries.FindAsync(second, Cancel))!.State);
        Assert.Equal(0, unwritten.Count);
    }

    private ServiceProvider Over(WebhookRig rig, TimeProvider clock)
        => database.Infrastructure(replace =>
        {
            replace.AddSingleton(clock);
            replace.AddSingleton(rig.Line);
        });

    private static async Task<NotificationDestination> RegisteredAsync(ServiceProvider services)
    {
        NotificationDestination destination = NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName("Somewhere"),
            WebhookRig.Slack.Target,
            services.GetRequiredService<WebhookUrlSeal>().Seal(WebhookRig.Slack),
            WebhookRig.Slack.Tail,
            Epoch);

        await using AsyncServiceScope registering = services.CreateAsyncScope();
        await registering.ServiceProvider.GetRequiredService<INotificationDestinationRepository>().AddAsync(destination, Cancel);

        return destination;
    }

    private static async Task<NotificationDeliveryId> HeldAsync(
        ServiceProvider services,
        NotificationDestinationId destination,
        DateTime occurredAt)
    {
        Notice notice = Notice.Take(
            NoticeId.New(),
            NoticeKind.RecordingCompleted,
            Guid.NewGuid(),
            occurredAt,
            new NoticeWords("録画が終わりました", "作り物の番組\n作り物の局", Link: null),
            occurredAt);
        NotificationDelivery delivery = NotificationDelivery.Queue(NotificationDeliveryId.New(), notice.Id, destination, occurredAt);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INoticeRepository>().AddAsync(notice, [delivery], Cancel);

        return delivery.Id;
    }
}
