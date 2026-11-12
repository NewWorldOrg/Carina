namespace Carina.Domain.Channels;

/// <summary>
/// Decides what a broadcast link between a station and its logo changes, looking up the station: a link to a station
/// not held is dropped, and a link that says what the station already says changes nothing.
/// </summary>
public sealed class StationLogoDomainService(IBroadcastServiceRepository services)
{
    /// <summary>
    /// The station the link moved, naming the logo or saying no picture is broadcast, or nothing when the station is not
    /// held or already said so.
    /// </summary>
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
