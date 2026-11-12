using System.Buffers.Text;
using System.Security.Cryptography;

using Carina.Domain.Notifications;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Tests.Notifications;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class PushDestinationRepositoryTests(RepositoryDatabase database)
{
    private const string Secret = "AAECAwQFBgcICQoLDA0ODw";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Epoch = new(2031, 6, 1, 12, 0, 0, DateTimeKind.Utc);

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

        await using (CarinaDbContext adding = database.Open())
        {
            await new NotificationDestinationRepository(adding).AddAsync(browser, Cancel);
        }

        await using CarinaDbContext context = database.Open();
        NotificationDestinationRepository rows = new(context);

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

        await using (CarinaDbContext switching = database.Open())
        {
            NotificationDestinationRepository rows = new(switching);
            NotificationDestination held = (await rows.FindAsync(first, Cancel))!;
            held.Disable(Epoch.AddMinutes(1));
            await rows.SaveAsync(held, Cancel);
        }

        NotificationDestinationId second = await EnrolledAsync("Named again", subscription, [0x02, 0x03], Epoch.AddMinutes(2));

        await using CarinaDbContext context = database.Open();
        NotificationDestination kept = (await new NotificationDestinationRepository(context).FindAsync(first, Cancel))!;

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

        await using CarinaDbContext context = database.Open();
        NotificationDestinationRepository rows = new(context);
        NotificationDestination held = (await rows.FindAsync(other, Cancel))!;
        held.Resubscribe(taken, [0x04], Epoch.AddMinutes(1));

        DbUpdateException refused = await Assert.ThrowsAsync<DbUpdateException>(() => rows.SaveAsync(held, Cancel));

        PostgresException inner = Assert.IsType<PostgresException>(refused.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, inner.SqlState);
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
        await using CarinaDbContext context = database.Open();
        NotificationDestinationRepository destinations = new(context);
        NotificationDestinationDomainService enrolling = new(
            destinations,
            new NotificationRuleRepository(context),
            new NotificationDeliveryRepository(context),
            new ProgrammeWatchRepository(context));

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
}
