namespace Carina.Domain.Channels;

public interface IServiceTuningDirectory
{
    Task<TuningResolution> ResolveTuningAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken);

    Task<bool> CanTuneAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken);
}
