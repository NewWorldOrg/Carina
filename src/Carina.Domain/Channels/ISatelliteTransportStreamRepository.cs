namespace Carina.Domain.Channels;

public interface ISatelliteTransportStreamRepository
{
    Task<IReadOnlyList<SatelliteTransportStream>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SatelliteTransportStream>> ListForSlotAsync(
        int bsChannel,
        CancellationToken cancellationToken);

    Task ReplaceSlotAsync(
        int bsChannel,
        IReadOnlyList<SatelliteTransportStream> streams,
        CancellationToken cancellationToken);
}
