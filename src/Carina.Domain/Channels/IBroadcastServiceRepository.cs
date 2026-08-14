namespace Carina.Domain.Channels;

public interface IBroadcastServiceRepository
{
    Task<BroadcastService?> FindAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BroadcastService>> ListAsync(CancellationToken cancellationToken);

    Task AddAsync(BroadcastService service, CancellationToken cancellationToken);

    Task SaveAsync(BroadcastService service, CancellationToken cancellationToken);

    Task RemoveAsync(NetworkId networkId, ServiceId serviceId, CancellationToken cancellationToken);
}
