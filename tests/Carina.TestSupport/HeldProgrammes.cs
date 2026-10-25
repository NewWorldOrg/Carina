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

    public Task<Programme?> FindUnlessSupersededAsync(ProgrammeId id, CancellationToken cancellationToken)
        => Task.FromResult(Standing().FirstOrDefault(programme => programme.Id.Equals(id)));

    public Task<IReadOnlyList<Programme>> ListAsync(
        ProgrammeWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        return Task.FromResult<IReadOnlyList<Programme>>(
        [
            .. Standing().Where(programme =>
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
            .. Standing()
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
        IReadOnlyList<ProgrammeService> heardWhole,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(broadcasts);
        ArgumentNullException.ThrowIfNull(heardWhole);

        var whole = heardWhole.Select(service => (service.NetworkId, service.ServiceId)).ToHashSet();
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
                held = discovered;
                added++;
            }
            else if (held.Absorb(broadcast, at))
            {
                held.MarkRevision(++handedOut);
                updated++;
            }

            if (whole.Contains((broadcast.Id.NetworkId.Value, broadcast.Id.ServiceId.Value)))
            {
                held.Heard(at);
            }
        }

        return Task.FromResult(new ProgrammesAbsorbed(added, updated));
    }

    public Task<DateTime?> HeardWholeAtAsync(
        int networkId,
        int serviceId,
        CancellationToken cancellationToken)
        => Task.FromResult(Programmes
            .Where(programme => programme.NetworkId.Value == networkId
                && programme.ServiceId.Value == serviceId)
            .Max(programme => programme.LastHeardAt));

    public Task<IReadOnlyList<EndedProgramme>> ListEndedBeforeAsync(
        DateTime at,
        int rows,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<EndedProgramme>>(
        [
            .. Programmes
                .Select(programme => (Programme: programme, EndedAt: programme.EndsAt ?? Programmes
                    .Where(next => next.Id.NetworkId.Equals(programme.Id.NetworkId)
                        && next.Id.ServiceId.Equals(programme.Id.ServiceId)
                        && next.StartsAt > programme.StartsAt)
                    .Min(next => (DateTime?)next.StartsAt)))
                .Where(ended => ended.EndedAt < at)
                .OrderBy(ended => ended.EndedAt)
                .Select(ended => new EndedProgramme(ended.Programme, ended.EndedAt!.Value))
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
                && programme.ServiceId.Value == serviceId)
            .Max(programme => (DateTime?)(programme.EndsAt ?? programme.StartsAt)));

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

    public Task<IReadOnlyList<Programme>> ListGroupedAsync(CancellationToken cancellationToken)
    {
        Programme[] linking =
        [
            .. Programmes.Where(programme => programme.Related.Any(related =>
                related.Kind is RelationKind.Relayed or RelationKind.Moved)),
        ];
        HashSet<(int, int, int)> named =
        [
            .. linking
                .SelectMany(programme => programme.Related)
                .Where(related => related.Kind is RelationKind.Relayed or RelationKind.Moved)
                .Select(related => (related.NetworkId, related.ServiceId, related.EventId)),
        ];

        HashSet<(int, int, int)> simulcast = Simulcasts();

        return Task.FromResult<IReadOnlyList<Programme>>(
        [
            .. linking,
            .. Programmes
                .Where(programme => !linking.Contains(programme))
                .Where(programme => named.Contains(Key(programme)) || simulcast.Contains(Key(programme))),
        ]);
    }

    private HashSet<(int, int, int)> Simulcasts()
    {
        Dictionary<(int, int, int), Programme> heard = [];

        foreach (Programme programme in Programmes.Where(programme => !programme.IsShadow))
        {
            heard.TryAdd(Key(programme), programme);
        }

        HashSet<(int, int, int)> found = [];

        foreach (Programme one in heard.Values)
        {
            foreach (RelatedProgramme related in one.Related.Where(related => related.Kind is RelationKind.Shared
                && related.ServiceId != one.ServiceId.Value
                && heard.ContainsKey((related.NetworkId, related.ServiceId, related.EventId))))
            {
                found.Add(Key(one));
                found.Add((related.NetworkId, related.ServiceId, related.EventId));
            }
        }

        IEnumerable<IGrouping<(int, int, DateTime, DateTime?, string), Programme>> alike = heard.Values
            .Where(programme => programme.Name.Length > 0 && programme.EndsAt is not null)
            .GroupBy(programme => (
                programme.NetworkId.Value,
                programme.TransportStreamId.Value,
                programme.StartsAt,
                programme.EndsAt,
                programme.Name));

        foreach (IGrouping<(int, int, DateTime, DateTime?, string), Programme> carried in alike
            .Where(carried => carried.Select(programme => programme.ServiceId.Value).Distinct().Count() > 1))
        {
            found.UnionWith(carried.Select(Key));
        }

        return found;
    }

    private static (int, int, int) Key(Programme programme)
        => (programme.NetworkId.Value, programme.ServiceId.Value, programme.EventId.Value);

    public Task<int> ForgetEverythingAsync(CancellationToken cancellationToken)
    {
        Wiped++;
        Programmes.Clear();

        return Task.FromResult(0);
    }

    public IReadOnlyList<Programme> Standing() => ProgrammeSupersession.Standing(Programmes);
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

public sealed class HeldArchive(HeldProgrammes? guide = null) : IArchivedProgrammeRepository
{
    private readonly HeldProgrammes held = guide ?? new HeldProgrammes();

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
            .. Standing()
                .Where(programme => wanted.Contains((programme.NetworkId.Value, programme.ServiceId.Value)))
                .Where(programme => programme.StartsAt < to && programme.EndsAt > from)
                .OrderBy(programme => programme.StartsAt),
        ]);
    }

    public IReadOnlyList<ArchivedProgramme> Standing() => ProgrammeSupersession.Standing(Programmes, held.Programmes);

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
    {
        HashSet<ProgrammeId> standing = [.. programmes.Standing().Select(programme => programme.Id)];
        HashSet<(ProgrammeId, DateTime)> kept =
            [.. archive.Standing().Select(programme => (Id(programme), programme.StartsAt))];

        return Task.FromResult(ProgrammeSearchMatching.Search(
            ProgrammeSearchMatching.Layered(programmes.Programmes, archive.Programmes)
                .Where(match => match.IsArchived ? kept.Contains((match.Id, match.StartsAt)) : standing.Contains(match.Id)),
            search,
            now));
    }

    private static ProgrammeId Id(ArchivedProgramme programme)
        => new(programme.NetworkId, programme.ServiceId, programme.EventId);
}
