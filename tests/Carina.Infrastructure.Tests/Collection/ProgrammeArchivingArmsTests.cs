using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Collection;

/// <summary>
/// The queries that pick what leaves the guide and what the archive lets go of, held to the stand-ins the domain
/// tests use for the same rows. Each row ends a moment before, exactly on, or a moment after the line the domain
/// draws, and a programme whose end was never told ends where the next one of its service begins.
/// </summary>
[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeArchivingArmsTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Moment = TimeSpan.FromMicroseconds(1);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly CollectionSettings Settings = new()
    {
        KeepEndedProgrammes = TimeSpan.FromHours(24),
        ArchiveRetention = TimeSpan.FromDays(30),
    };

    [Fact(DisplayName = "BR-ED-009: on either side of the line and exactly on it, the store and the stand-in send the same programmes to the archive")]
    public async Task TheStoreAndTheStandInSendTheSameProgrammesToTheArchive()
    {
        DateTime line = Now - Settings.KeepEndedProgrammes;
        var held = new HeldProgrammes();
        await using CarinaDbContext context = database.Open();

        await EmptyAsync(context);

        int network = BroadcastIds.NextNetwork();

        held.Programmes.AddRange(Laid(network, line));
        await context.AddRangeAsync(Laid(network, line), Cancel);
        await context.SaveChangesAsync(Cancel);

        IReadOnlyList<EndedProgramme> fromTheStore = await new ProgrammeArchivingDomainService(
                new ProgrammeRepository(context),
                new ArchivedProgrammeRepository(context))
            .ListLeavingAsync(Settings, Now, Cancel);
        IReadOnlyList<EndedProgramme> fromTheStandIn = await new ProgrammeArchivingDomainService(held, new HeldArchive(held))
            .ListLeavingAsync(Settings, Now, Cancel);

        Assert.Equal([1, 4], Leaving(fromTheStandIn).Select(leaving => leaving.Event));
        Assert.Equal(Leaving(fromTheStandIn), Leaving(fromTheStore));

        await EmptyAsync(context);
    }

    [Fact(DisplayName = "BR-ED-009: on either side of the retention and exactly on it, the store and the stand-in let go of the same archived programmes")]
    public async Task TheStoreAndTheStandInLetGoOfTheSameArchivedProgrammes()
    {
        DateTime line = Now - Settings.ArchiveRetention!.Value;
        int network = BroadcastIds.NextNetwork();
        var held = new HeldArchive();
        await using CarinaDbContext context = database.Open();

        await EmptyAsync(context);

        TimeSpan[] offsets = [-Moment, TimeSpan.Zero, Moment];

        for (int carried = 1; carried <= offsets.Length; carried++)
        {
            held.Programmes.Add(Archived(network, carried, line + offsets[carried - 1]));
        }

        await new ArchivedProgrammeRepository(context).KeepAsync(
            [.. offsets.Select((offset, index) => Archived(network, index + 1, line + offset))],
            Cancel);

        int forgottenByTheStore = await new ProgrammeArchivingDomainService(
                new ProgrammeRepository(context),
                new ArchivedProgrammeRepository(context))
            .ForgetExpiredAsync(Settings, Now, Cancel);
        int forgottenByTheStandIn = await new ProgrammeArchivingDomainService(new HeldProgrammes(), held)
            .ForgetExpiredAsync(Settings, Now, Cancel);

        await using CarinaDbContext reading = database.Open();
        int[] standing = [.. (await reading.Set<ArchivedProgramme>().ToListAsync(Cancel)).Select(kept => kept.EventId.Value).Order()];

        Assert.Equal(1, forgottenByTheStandIn);
        Assert.Equal(forgottenByTheStandIn, forgottenByTheStore);
        Assert.Equal([.. held.Programmes.Select(kept => kept.EventId.Value).Order()], standing);

        await EmptyAsync(reading);
    }

    private static IEnumerable<Programme> Laid(int network, DateTime line)
    {
        yield return Ended(network, 1, line - Moment - TimeSpan.FromMinutes(30), line - Moment);
        yield return Ended(network, 2, line - TimeSpan.FromMinutes(30), line);
        yield return Ended(network, 3, line + Moment - TimeSpan.FromMinutes(30), line + Moment);

        yield return Ended(network, 4, line - TimeSpan.FromHours(2), null, serviceId: 2001);
        yield return Ended(network, 5, line - Moment, null, serviceId: 2001);

        yield return Ended(network, 6, line - TimeSpan.FromHours(2), null, serviceId: 2002);
        yield return Ended(network, 7, line, null, serviceId: 2002);
    }

    private static Programme Ended(int network, int carried, DateTime startsAt, DateTime? endsAt, int serviceId = 1049)
        => Programme.Discover(
            new ProgrammeBroadcast(
                new ProgrammeId(new NetworkId(network), new ServiceId(serviceId), new EventId(carried)),
                new TransportStreamId(32_736),
                startsAt,
                endsAt,
                "ニュース",
                string.Empty,
                false),
            startsAt);

    private static ArchivedProgramme Archived(int network, int carried, DateTime endsAt)
        => ArchivedProgramme.Of(
            new EndedProgramme(Ended(network, carried, endsAt - TimeSpan.FromMinutes(30), endsAt), endsAt),
            endsAt)!;

    private static IReadOnlyList<(int Event, DateTime EndedAt)> Leaving(IReadOnlyList<EndedProgramme> leaving)
        =>
        [
            .. leaving
                .Select(ended => (Event: ended.Programme.Id.EventId.Value, ended.EndedAt))
                .OrderBy(ended => ended.Event),
        ];

    private static async Task EmptyAsync(CarinaDbContext context)
    {
        await context.Set<Programme>().ExecuteDeleteAsync(Cancel);
        await context.Set<ArchivedProgramme>().ExecuteDeleteAsync(Cancel);
    }
}
