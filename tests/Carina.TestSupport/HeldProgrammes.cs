using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.TestSupport;

public sealed class HeldProgrammes : IProgrammeRepository
{
    public List<Programme> Programmes { get; } = [];

    public int Wiped { get; private set; }

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

    public Task<int> ForgetEndedBeforeAsync(DateTime at, CancellationToken cancellationToken)
        => Task.FromResult(0);

    public Task<DateTime?> CoveredUntilAsync(
        int networkId,
        int serviceId,
        CancellationToken cancellationToken)
        => Task.FromResult<DateTime?>(null);

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
