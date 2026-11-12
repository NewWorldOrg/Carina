using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Tests.DataBroadcast;
using Carina.Infrastructure.Tests.Notifications;
using Carina.TestSupport;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

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
        EphemeralDataProtectionProvider keys = new();
        WebhookUrlSeal seal = new(keys);
        NotificationDestination destination = NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName("Somewhere"),
            WebhookRig.Slack.Target,
            seal.Seal(WebhookRig.Slack),
            WebhookRig.Slack.Tail,
            Epoch);

        await using (CarinaDbContext registering = database.Open())
        {
            await new NotificationDestinationRepository(registering).AddAsync(destination, Cancel);
        }

        NotificationDeliveryId first = await HeldAsync(destination.Id, Epoch.AddMinutes(-2));
        NotificationDeliveryId second = await HeldAsync(destination.Id, Epoch.AddMinutes(-1));
        NotificationDeliverySettings settings = new();
        HandTurnedClock clock = new(new DateTimeOffset(Epoch));
        using PushDesk push = new(keys, clock);

        NotificationDeliveries moved;
        await using (CarinaDbContext passing = database.Open())
        {
            NotificationDeliveryRound round = new(
                new NotificationDeliveryRepository(passing),
                new NotificationDestinationRepository(passing),
                new NotificationDeliveryDomainService(),
                seal,
                push.Seal,
                push.Messages,
                push.Handling,
                new NoticeDispatch(rig.Line, settings, clock),
                new UnwrittenDeliveries(),
                settings,
                clock,
                NullLogger<NotificationDeliveryRound>.Instance);
            moved = await round.SendAsync(Cancel);
        }

        await using CarinaDbContext context = database.Open();
        NotificationDeliveryRepository deliveries = new(context);
        NotificationDestination held = (await new NotificationDestinationRepository(context).FindAsync(destination.Id, Cancel))!;

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
        EphemeralDataProtectionProvider keys = new();
        WebhookUrlSeal seal = new(keys);
        NotificationDestination destination = NotificationDestination.Register(
            NotificationDestinationId.New(),
            new NotificationName("Somewhere"),
            WebhookRig.Slack.Target,
            seal.Seal(WebhookRig.Slack),
            WebhookRig.Slack.Tail,
            Epoch);

        await using (CarinaDbContext registering = database.Open())
        {
            await new NotificationDestinationRepository(registering).AddAsync(destination, Cancel);
        }

        NotificationDeliveryId first = await HeldAsync(destination.Id, Epoch.AddMinutes(-2));
        NotificationDeliveryId second = await HeldAsync(destination.Id, Epoch.AddMinutes(-1));
        removedOnTheWay = first;
        NotificationDeliverySettings settings = new();
        HandTurnedClock clock = new(new DateTimeOffset(Epoch));
        using PushDesk push = new(keys, clock);
        UnwrittenDeliveries unwritten = new();

        await using (CarinaDbContext passing = database.Open())
        {
            NotificationDeliveryRound round = new(
                new NotificationDeliveryRepository(passing),
                new NotificationDestinationRepository(passing),
                new NotificationDeliveryDomainService(),
                seal,
                push.Seal,
                push.Messages,
                push.Handling,
                new NoticeDispatch(rig.Line, settings, clock),
                unwritten,
                settings,
                clock,
                NullLogger<NotificationDeliveryRound>.Instance);
            await round.SendAsync(Cancel);
        }

        await using CarinaDbContext context = database.Open();
        NotificationDeliveryRepository deliveries = new(context);

        Assert.Equal(2, rig.Server.Heard.Count);
        Assert.Null(await deliveries.FindAsync(first, Cancel));
        Assert.Equal(NotificationDeliveryState.Sent, (await deliveries.FindAsync(second, Cancel))!.State);
        Assert.Equal(0, unwritten.Count);
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

        await using CarinaDbContext context = database.Open();
        await new NoticeRepository(context).AddAsync(notice, [delivery], Cancel);

        return delivery.Id;
    }
}
