namespace Carina.Domain.Channels;

public sealed class StationLogoDomainService(IBroadcastServiceRepository services)
{
    public async Task<BroadcastService?> LinkAsync(
        NetworkId networkId,
        ServiceId serviceId,
        LogoId? logoId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);

        if (await services.FindAsync(networkId, serviceId, cancellationToken) is not { } service)
        {
            return null;
        }

        bool moved = logoId is not null ? service.NamesTheLogo(logoId) : service.BroadcastsNoLogo();

        return moved ? service : null;
    }
}
