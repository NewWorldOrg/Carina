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

    public Task SaveAsync(Programme programme, CancellationToken cancellationToken)
        => Task.CompletedTask;

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

    public Task<PaginatedList<Programme>> SearchAsync(
        ProgrammeSearch search,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        Programme[] found =
        [
            .. Programmes
                .Where(programme => search.Words.All(word => Carries(programme, word, search.Fields)))
                .Where(programme => !search.ExcludedWords.Any(word => Carries(programme, word, search.Fields)))
                .Where(programme => search.Genres.Count == 0
                    || programme.Genres.Any(genre => search.Genres.Contains(genre.Kind)))
                .Where(programme => search.Channels.Count == 0 || On(programme, search.Channels))
                .Where(programme => search.Services is not { } within || On(programme, within))
                .Where(programme => search.From is not { } from
                    || programme.EndsAt is null
                    || programme.EndsAt > from)
                .Where(programme => search.To is not { } to || programme.StartsAt < to)
                .OrderBy(programme => programme.StartsAt),
        ];

        return Task.FromResult(new PaginatedList<Programme>(
            [.. found.Skip((search.Page - 1) * search.PerPage).Take(search.PerPage)],
            found.Length,
            search.Page,
            search.PerPage));
    }

    private static bool Carries(Programme programme, string word, IReadOnlyList<ProgrammeField> fields)
        => (fields.Contains(ProgrammeField.Title)
                && programme.Name.Contains(word, StringComparison.OrdinalIgnoreCase))
            || (fields.Contains(ProgrammeField.Description)
                && programme.Summary.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static bool On(Programme programme, IReadOnlyList<ProgrammeService> services)
        => services.Any(service => service.NetworkId == programme.NetworkId.Value
            && service.ServiceId == programme.ServiceId.Value);

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

    public Task<long> NextRevisionAsync(CancellationToken cancellationToken)
        => Task.FromResult(++handedOut);

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
