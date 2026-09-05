namespace Carina.Domain.Channels;

public interface IStationLogoRepository
{
    Task<StationLogo?> FindAsync(NetworkId networkId, LogoId logoId, CancellationToken cancellationToken);

    Task<StationLogo?> OfServiceAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StationLogo>> ListAsync(CancellationToken cancellationToken);

    Task AbsorbAsync(StationLogo logo, CancellationToken cancellationToken);
}
