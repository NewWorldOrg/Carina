using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using EventId = Carina.Domain.Programmes.EventId;

namespace Carina.Infrastructure.Tests.Notifications;

/// <summary>
/// Writes what the guide says of a made-up programme and the row asking to be told of it into the scratch
/// database, and looks at the rows as the loop does.
/// </summary>
internal sealed class WatchedProgrammes(RepositoryDatabase database)
{
    public const string ProgrammeName = "A made-up programme";

    public const string StationName = "Made-up Television";

    public static readonly TimeSpan Length = TimeSpan.FromMinutes(30);

    private const int Network = 32736;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static int nextService = 61000;

    public async Task<ProgrammeId> AnnouncedAsync(DateTime startsAt, DateTime heardAt)
    {
        ProgrammeId programme = new(
            new NetworkId(Network),
            new ServiceId(Interlocked.Increment(ref nextService)),
            new EventId(1));

        await using (CarinaDbContext context = database.Open())
        {
            await new BroadcastServiceRepository(context).AddAsync(
                BroadcastService.Discover(
                    programme.NetworkId,
                    programme.ServiceId,
                    StationName,
                    ServiceCategory.Television,
                    heardAt),
                Cancel);
        }

        await HeardAsync(programme, startsAt, heardAt);

        return programme;
    }

    public Task MovedAsync(ProgrammeId programme, DateTime startsAt, DateTime heardAt)
        => HeardAsync(programme, startsAt, heardAt);

    public Task DroppedAsync(ProgrammeId programme, DateTime heardAt)
        => HeardAsync(
            new ProgrammeId(programme.NetworkId, programme.ServiceId, new EventId(programme.EventId.Value + 1)),
            heardAt.AddDays(1),
            heardAt);

    public async Task<ProgrammeWatchId> AskedAsync(
        ProgrammeId programme,
        DateTime at,
        int leadMinutes,
        params NotificationDestinationId[] destinations)
    {
        await using CarinaDbContext context = database.Open();
        Programme announced = (await new ProgrammeRepository(context).FindAsync(programme, Cancel))!;
        ProgrammeWatch watch = ProgrammeWatch.Ask(
            ProgrammeWatchId.New(),
            programme,
            WatchedBroadcast.Of(announced, StationName),
            leadMinutes,
            destinations,
            at);

        await new ProgrammeWatchRepository(context).AddAsync(watch, Cancel);

        return watch.Id;
    }

    public async Task WithdrawnAsync(ProgrammeWatchId id, DateTime at)
    {
        await using CarinaDbContext context = database.Open();
        ProgrammeWatchRepository watches = new(context);
        ProgrammeWatch watch = (await watches.FindAsync(id, Cancel))!;

        watch.Cancel(at);
        await watches.SaveAsync(watch, Cancel);
    }

    public async Task DestinationRemovedAsync(NotificationDestinationId id, DateTime at)
    {
        await using CarinaDbContext context = database.Open();
        ProgrammeWatchRepository watches = new(context);
        NotificationDestinationRepository destinations = new(context);

        foreach (ProgrammeWatch watch in await watches.ListFollowedAsync(at, Cancel))
        {
            if (watch.StopTelling(id, at))
            {
                await watches.SaveAsync(watch, Cancel);
            }
        }

        await destinations.RemoveAsync((await destinations.FindAsync(id, Cancel))!, Cancel);
    }

    public async Task<ProgrammeWatch> RowAsync(ProgrammeWatchId id)
    {
        await using CarinaDbContext context = database.Open();

        return (await new ProgrammeWatchRepository(context).FindAsync(id, Cancel))!;
    }

    private async Task HeardAsync(ProgrammeId programme, DateTime startsAt, DateTime heardAt)
    {
        await using CarinaDbContext context = database.Open();

        await new ProgrammeRepository(context).AbsorbAsync(
            [new ProgrammeBroadcast(programme, new TransportStreamId(Network), startsAt, startsAt + Length, ProgrammeName, string.Empty, false)],
            [new ProgrammeService(programme.NetworkId.Value, programme.ServiceId.Value)],
            heardAt,
            Cancel);
    }
}
