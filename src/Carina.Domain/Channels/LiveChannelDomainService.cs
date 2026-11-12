namespace Carina.Domain.Channels;

/// <summary>
/// Decides which services can be watched live: a television service whose channel has been selected.
/// </summary>
public sealed class LiveChannelDomainService(
    IBroadcastServiceRepository services,
    ICandidateChannelRepository candidates)
{
    public async Task<WatchableChannel?> FindAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);

        if (await services.FindAsync(networkId, serviceId, cancellationToken) is not { WatchableLive: true } service
            || await candidates.FindSelectedAsync(networkId, serviceId, cancellationToken) is not { } selected)
        {
            return null;
        }

        return new WatchableChannel(service, selected);
    }

    public async Task<IReadOnlyList<WatchableChannel>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CandidateChannel> selected = await candidates.ListSelectedAsync(cancellationToken);
        List<WatchableChannel> watchable = [];

        foreach (BroadcastService service in await services.ListAsync(cancellationToken))
        {
            if (!service.WatchableLive)
            {
                continue;
            }

            if (selected.FirstOrDefault(candidate => candidate.BelongsTo(service.NetworkId, service.ServiceId)) is { } tuned)
            {
                watchable.Add(new WatchableChannel(service, tuned));
            }
        }

        return watchable;
    }
}
