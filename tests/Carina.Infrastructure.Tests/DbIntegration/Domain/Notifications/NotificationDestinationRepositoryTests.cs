using System.Buffers.Text;
using System.Security.Cryptography;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Notifications;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Notifications;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class NotificationDestinationRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private const string Secret = "AAECAwQFBgcICQoLDA0ODw";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Epoch = new(2031, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly NoticeLedgers ledgers = new(database);

    private readonly ServiceProvider services = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await services.DisposeAsync();

    [Fact(DisplayName = "BR-ND-015: a browser is kept with the hash of where it is reached, and is found by that hash alone")]
    public async Task ABrowserIsFoundByTheHashOfWhereItIsReached()
    {
        PushSubscription subscription = Subscription();
        NotificationDestination browser = NotificationDestination.Subscribe(
            NotificationDestinationId.New(),
            new NotificationName("A browser"),
            subscription,
            [0x01, 0x02],
            Epoch);

        await using (AsyncServiceScope adding = services.CreateAsyncScope())
        {
            await Rows(adding).AddAsync(browser, Cancel);
        }

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        INotificationDestinationRepository rows = Rows(scope);

        NotificationDestination? found = await rows.FindByEndpointHashAsync(subscription.Fingerprint, Cancel);
        NotificationDestination? none = await rows.FindByEndpointHashAsync(Subscription().Fingerprint, Cancel);

        Assert.NotNull(found);
        Assert.Equal(browser.Id, found.Id);
        Assert.Equal(WebhookTarget.Push, found.Target);
        Assert.Equal(subscription.Fingerprint, found.EndpointHash);
        Assert.Equal(subscription.Tail, found.UrlTail);
        Assert.Equal([0x01, 0x02], found.SealedUrl);
        Assert.Null(none);
    }

    [Fact(DisplayName = "BR-ND-015: a browser registered again from the same place stays one row with its name and its on and off, and holds what was sealed last")]
    public async Task ABrowserRegisteredAgainStaysOneRow()
    {
        PushSubscription subscription = Subscription();
        NotificationDestinationId first = await EnrolledAsync("At first", subscription, [0x01], Epoch);

        await using (AsyncServiceScope switching = services.CreateAsyncScope())
        {
            INotificationDestinationRepository rows = Rows(switching);
            NotificationDestination held = (await rows.FindAsync(first, Cancel))!;
            held.Disable(Epoch.AddMinutes(1));
            await rows.SaveAsync(held, Cancel);
        }

        NotificationDestinationId second = await EnrolledAsync("Named again", subscription, [0x02, 0x03], Epoch.AddMinutes(2));

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        NotificationDestination kept = (await Rows(scope).FindAsync(first, Cancel))!;
        await using CarinaDbContext context = database.Open();

        Assert.Equal(first, second);
        Assert.Equal(1, await context.Set<NotificationDestination>().CountAsync(row => row.EndpointHash == subscription.Fingerprint, Cancel));
        Assert.Equal("At first", kept.Name.Value);
        Assert.False(kept.Enabled);
        Assert.Equal([0x02, 0x03], kept.SealedUrl);
        Assert.Equal(Epoch.AddMinutes(2), kept.ChangedAt);
    }

    [Fact(DisplayName = "BR-ND-015: a browser pointed at where another row is already reached is refused by the ledger itself")]
    public async Task ABrowserPointedAtWhereAnotherRowIsReachedIsRefusedByTheLedger()
    {
        PushSubscription taken = Subscription();
        await EnrolledAsync("Holding", taken, [0x01], Epoch);
        NotificationDestinationId other = await EnrolledAsync("Another", Subscription(), [0x01], Epoch);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        INotificationDestinationRepository rows = Rows(scope);
        NotificationDestination held = (await rows.FindAsync(other, Cancel))!;
        held.Resubscribe(taken, [0x04], Epoch.AddMinutes(1));

        DbUpdateException refused = await Assert.ThrowsAsync<DbUpdateException>(() => rows.SaveAsync(held, Cancel));

        PostgresException inner = Assert.IsType<PostgresException>(refused.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, inner.SqlState);
    }

    [Fact(DisplayName = "BR-NS-002: keeping a result leaves a new name alone and does not turn a destination back on")]
    public async Task KeepingAResultLeavesANewNameAloneAndDoesNotTurnADestinationBackOn()
    {
        await ledgers.EmptyTheNoticesAsync();
        NotificationDestinationId id = await ledgers.DestinationAsync("Before", Epoch);

        await using AsyncServiceScope sending = services.CreateAsyncScope();
        INotificationDestinationRepository asSending = Rows(sending);
        NotificationDestination seenBySending = (await asSending.FindAsync(id, Cancel))!;

        await using (AsyncServiceScope changing = services.CreateAsyncScope())
        {
            INotificationDestinationRepository asChanging = Rows(changing);
            NotificationDestination seenByChanging = (await asChanging.FindAsync(id, Cancel))!;
            seenByChanging.Rename(new NotificationName("After"), Epoch.AddMinutes(1));
            seenByChanging.Disable(Epoch.AddMinutes(1));
            await asChanging.SaveAsync(seenByChanging, Cancel);
        }

        seenBySending.NoteDelivered(Epoch.AddMinutes(2));
        await asSending.KeepResultAsync(seenBySending, Cancel);

        await using AsyncServiceScope reading = services.CreateAsyncScope();
        NotificationDestination held = (await Rows(reading).FindAsync(id, Cancel))!;

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

        await using (AsyncServiceScope sending = services.CreateAsyncScope())
        {
            INotificationDestinationRepository asSending = Rows(sending);
            NotificationDestination seen = (await asSending.FindAsync(id, Cancel))!;
            seen.NoteFailed(NoticeFailure.Rejected, Epoch.AddMinutes(2));
            await asSending.KeepResultAsync(seen, Cancel);
        }

        await using AsyncServiceScope reading = services.CreateAsyncScope();
        NotificationDestination held = (await Rows(reading).FindAsync(id, Cancel))!;

        Assert.False(held.Enabled);
        Assert.Equal(NoticeFailure.Rejected, held.LastFailure);
    }

    private static PushSubscription Subscription()
    {
        using ECDiffieHellman pair = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        ECPoint point = pair.ExportParameters(includePrivateParameters: false).Q;
        byte[] key = [0x04, .. point.X!, .. point.Y!];

        return PushSubscription.Read(
            $"https://updates.push.services.mozilla.com/wpush/v2/{Guid.NewGuid():N}",
            Base64Url.EncodeToString(key),
            Secret)!;
    }

    private async Task<NotificationDestinationId> EnrolledAsync(string name, PushSubscription subscription, byte[] sealedSubscription, DateTime at)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        INotificationDestinationRepository destinations = Rows(scope);
        NotificationDestinationDomainService enrolling = scope.ServiceProvider.GetRequiredService<NotificationDestinationDomainService>();

        PushEnrolment enrolment = await enrolling.EnrolAsync(new NotificationName(name), subscription, sealedSubscription, at, Cancel);

        if (enrolment.Added)
        {
            await destinations.AddAsync(enrolment.Destination, Cancel);
        }
        else
        {
            await destinations.SaveAsync(enrolment.Destination, Cancel);
        }

        return enrolment.Destination.Id;
    }

    private static INotificationDestinationRepository Rows(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<INotificationDestinationRepository>();
}
