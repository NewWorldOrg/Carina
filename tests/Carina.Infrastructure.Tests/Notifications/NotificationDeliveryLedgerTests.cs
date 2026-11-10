using Carina.Domain.Notifications;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Tests.DataBroadcast;
using Carina.TestSupport;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Notifications;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class NotificationDeliveryLedgerTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Epoch = new(2031, 5, 6, 9, 0, 0, DateTimeKind.Utc);

    private readonly NoticeLedgers ledgers = new(database);

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

        await using CarinaDbContext context = database.Open();
        IReadOnlyList<NoticeOnItsWay> waiting = await new NotificationDeliveryRepository(context).WaitingAsync(Cancel);

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

        await using CarinaDbContext context = database.Open();
        IReadOnlyList<NoticeOnItsWay> latest = await new NotificationDeliveryRepository(context).LatestOfAsync(asked, 2, Cancel);

        Assert.Equal([third, second], latest.Select(row => row.Delivery.Id));
        Assert.Equal(
            [NotificationDeliveryState.GivenUp, NotificationDeliveryState.Sent],
            latest.Select(row => row.Delivery.State));
        Assert.Equal(NoticeFailure.Rejected, latest[0].Delivery.LastFailure);
    }

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
        await using (CarinaDbContext clearing = database.Open())
        {
            removed = await new NoticeRepository(clearing).RemoveBeforeAsync(edge, Cancel);
        }

        await using CarinaDbContext context = database.Open();
        NotificationDeliveryRepository deliveries = new(context);

        Assert.Equal(1, removed);
        Assert.Null(await deliveries.FindAsync(old, Cancel));
        Assert.NotNull(await deliveries.FindAsync(onTheEdge, Cancel));
        Assert.NotNull(await deliveries.FindAsync(recent, Cancel));
        Assert.Equal(2, await context.Set<Notice>().CountAsync(Cancel));
    }

    [Fact(DisplayName = "BR-NS-002: keeping a result leaves a new name alone and does not turn a destination back on")]
    public async Task KeepingAResultLeavesANewNameAloneAndDoesNotTurnADestinationBackOn()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDestinationId id = await ledgers.DestinationAsync("Before", Epoch);

        await using CarinaDbContext sending = database.Open();
        NotificationDestinationRepository asSending = new(sending);
        NotificationDestination seenBySending = (await asSending.FindAsync(id, Cancel))!;

        await using (CarinaDbContext changing = database.Open())
        {
            NotificationDestinationRepository asChanging = new(changing);
            NotificationDestination seenByChanging = (await asChanging.FindAsync(id, Cancel))!;
            seenByChanging.Rename(new NotificationName("After"), Epoch.AddMinutes(1));
            seenByChanging.Disable(Epoch.AddMinutes(1));
            await asChanging.SaveAsync(seenByChanging, Cancel);
        }

        seenBySending.NoteDelivered(Epoch.AddMinutes(2));
        await asSending.KeepResultAsync(seenBySending, Cancel);

        await using CarinaDbContext context = database.Open();
        NotificationDestination held = (await new NotificationDestinationRepository(context).FindAsync(id, Cancel))!;

        Assert.Equal("After", held.Name.Value);
        Assert.False(held.Enabled);
        Assert.Equal(Epoch.AddMinutes(2), held.LastResultAt);
        Assert.Null(held.LastFailure);
    }

    [Fact(DisplayName = "BR-NS-002: keeping the result of a rejected address turns the destination off")]
    public async Task KeepingTheResultOfARejectedAddressTurnsTheDestinationOff()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDestinationId id = await ledgers.DestinationAsync("Somewhere", Epoch);

        await using (CarinaDbContext sending = database.Open())
        {
            NotificationDestinationRepository asSending = new(sending);
            NotificationDestination seen = (await asSending.FindAsync(id, Cancel))!;
            seen.NoteFailed(NoticeFailure.Rejected, Epoch.AddMinutes(2));
            await asSending.KeepResultAsync(seen, Cancel);
        }

        await using CarinaDbContext context = database.Open();
        NotificationDestination held = (await new NotificationDestinationRepository(context).FindAsync(id, Cancel))!;

        Assert.False(held.Enabled);
        Assert.Equal(NoticeFailure.Rejected, held.LastFailure);
    }

    [Fact(DisplayName = "BR-NS-001: a pass over the ledger sends what waits and writes that it was sent")]
    public async Task APassOverTheLedgerSendsWhatWaitsAndWritesThatItWasSent()
    {
        await ledgers.EmptyTheNoticesAsync();
        await using WebhookRig rig = new(_ => CannedAnswer.Text(200, "ok"));
        WebhookUrlSeal seal = new(new EphemeralDataProtectionProvider());
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

        NotificationDeliveries moved;
        await using (CarinaDbContext passing = database.Open())
        {
            NotificationDeliveryRound round = new(
                new NotificationDeliveryRepository(passing),
                new NotificationDestinationRepository(passing),
                seal,
                new NoticeDispatch(rig.Line, settings, clock),
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
