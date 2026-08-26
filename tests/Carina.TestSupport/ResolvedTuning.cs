using Carina.Domain.Channels;

namespace Carina.TestSupport;

public sealed class ResolvedTuning(TuningResolution resolution) : IServiceTuningDirectory
{
    public List<int> Asked { get; } = [];

    public Task<TuningResolution> ResolveTuningAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
    {
        Asked.Add(serviceId.Value);

        return Task.FromResult(resolution);
    }

    public Task<bool> CanTuneAsync(NetworkId networkId, ServiceId serviceId, CancellationToken cancellationToken)
        => Task.FromResult(resolution.CanTune);
}
