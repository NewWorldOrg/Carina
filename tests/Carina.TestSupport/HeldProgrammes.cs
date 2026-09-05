using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.TestSupport;

public sealed class HeldProgrammes : IProgrammeRepository
{
    public List<Programme> Programmes { get; } = [];

    public int Wiped { get; private set; }

    private long handedOut;

    public Task<Programme?> FindAsync(ProgrammeId id, CancellationToken cancellationToken)
        => Task.FromResult(Programmes.FirstOrDefault(programme => programme.Id.Equals(id)));

    public Task<IReadOnlyList<Programme>> ListAsync(
        ProgrammeWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        return Task.FromResult<IReadOnlyList<Programme>>(
        [
            .. Programmes.Where(programme =>
                programme.Id.NetworkId.Value == window.NetworkId
                && programme.Id.ServiceId.Value == window.ServiceId),
        ]);
    }

    public Task<IReadOnlyList<Programme>> ListForServicesAsync(
        IReadOnlyList<ProgrammeService> services,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        var wanted = services.Select(service => (service.NetworkId, service.ServiceId)).ToHashSet();

        return Task.FromResult<IReadOnlyList<Programme>>(
        [
            .. Programmes
                .Where(programme => wanted.Contains((programme.NetworkId.Value, programme.ServiceId.Value)))
                .Where(programme => !programme.IsShadow)
                .Where(programme => programme.StartsAt < to)
                .Where(programme => programme.EndsAt is null || programme.EndsAt > from)
                .OrderBy(programme => programme.StartsAt)
                .ThenBy(programme => programme.EventId.Value),
        ]);
    }

    public Task AddAsync(Programme programme, CancellationToken cancellationToken)
    {
        Programmes.Add(programme);

        return Task.CompletedTask;
    }

    public Task<ProgrammesAbsorbed> AbsorbAsync(
        IReadOnlyList<ProgrammeBroadcast> broadcasts,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(broadcasts);

        int added = 0;
        int updated = 0;

        foreach (ProgrammeBroadcast broadcast in broadcasts)
        {
            Programme? held = Programmes.FirstOrDefault(programme => programme.Id.Equals(broadcast.Id));

            if (held is null)
            {
                Programme discovered = Programme.Discover(broadcast, at);

                discovered.MarkRevision(++handedOut);
                Programmes.Add(discovered);
                added++;

                continue;
            }

            if (held.Absorb(broadcast, at))
            {
                held.MarkRevision(++handedOut);
                updated++;
            }
        }

        return Task.FromResult(new ProgrammesAbsorbed(added, updated));
    }

    public Task<IReadOnlyList<Programme>> ListEndedBeforeAsync(
        DateTime at,
        int rows,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Programme>>(
        [
            .. Programmes
                .Where(programme => programme.EndsAt is { } endsAt && endsAt < at)
                .OrderBy(programme => programme.EndsAt)
                .Take(rows),
        ]);

    public Task<int> ForgetAsync(IReadOnlyList<Programme> programmes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programmes);

        var leaving = programmes.Select(programme => programme.Id).ToHashSet();

        return Task.FromResult(Programmes.RemoveAll(held => leaving.Contains(held.Id)));
    }

    public Task<DateTime?> CoveredUntilAsync(
        int networkId,
        int serviceId,
        CancellationToken cancellationToken)
        => Task.FromResult(Programmes
            .Where(programme => programme.NetworkId.Value == networkId
                && programme.ServiceId.Value == serviceId
                && !programme.IsShadow)
            .Max(programme => (DateTime?)programme.StartsAt));

    public Task<IReadOnlyList<Programme>> ListAfterAsync(
        long revision,
        int rows,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Programme>>(
        [
            .. Programmes
                .Where(programme => programme.Revision > revision)
                .OrderBy(programme => programme.Revision)
                .Take(rows),
        ]);

    public Task<int> ForgetEverythingAsync(CancellationToken cancellationToken)
    {
        Wiped++;
        Programmes.Clear();

        return Task.FromResult(0);
    }
}

public sealed class HeldEpochs : ICollectionEpochRepository
{
    private CollectionEpoch? held;

    public Task<CollectionEpoch> ReadAsync(DateTime at, CancellationToken cancellationToken)
        => Task.FromResult(held ??= CollectionEpoch.Begin(at));

    public Task SaveAsync(CollectionEpoch epoch, CancellationToken cancellationToken)
    {
        held = epoch;

        return Task.CompletedTask;
    }
}

public sealed class HeldArchive : IArchivedProgrammeRepository
{
    public List<ArchivedProgramme> Programmes { get; } = [];

    public Task<IReadOnlyList<ArchivedProgramme>> ListAsync(
        IReadOnlyList<ProgrammeService> services,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        var wanted = services.Select(service => (service.NetworkId, service.ServiceId)).ToHashSet();

        return Task.FromResult<IReadOnlyList<ArchivedProgramme>>(
        [
            .. Programmes
                .Where(programme => wanted.Contains((programme.NetworkId.Value, programme.ServiceId.Value)))
                .Where(programme => programme.StartsAt < to && programme.EndsAt > from)
                .OrderBy(programme => programme.StartsAt),
        ]);
    }

    public Task<int> KeepAsync(IReadOnlyList<ArchivedProgramme> programmes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programmes);

        Programmes.AddRange(programmes);

        return Task.FromResult(programmes.Count);
    }

    public Task<int> ForgetBeforeAsync(DateTime at, CancellationToken cancellationToken)
        => Task.FromResult(Programmes.RemoveAll(programme => programme.EndsAt < at));

    public Task<int> ForgetServiceAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
        => Task.FromResult(Programmes.RemoveAll(programme =>
            programme.NetworkId.Equals(networkId) && programme.ServiceId.Equals(serviceId)));
}

public sealed class HeldSearches(HeldProgrammes programmes, HeldArchive archive) : IProgrammeSearchRepository
{
    public Task<PaginatedList<ProgrammeMatch>> SearchAsync(
        ProgrammeSearch search,
        DateTime now,
        CancellationToken cancellationToken)
        => Task.FromResult(ProgrammeSearchMatching.Search(
            ProgrammeSearchMatching.Layered(programmes.Programmes, archive.Programmes),
            search,
            now));
}
