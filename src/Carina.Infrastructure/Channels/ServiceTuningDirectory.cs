using Carina.Contracts;
using Carina.Domain.Channels;

namespace Carina.Infrastructure.Channels;

public sealed class ServiceTuningDirectory(
    IBroadcastServiceRepository services,
    ICandidateChannelRepository candidates,
    ITunerCapacityDirectory capacity)
    : IServiceTuningDirectory
{
    public async Task<TuningResolution> ResolveTuningAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
    {
        if (await services.FindAsync(networkId, serviceId, cancellationToken) is null)
        {
            return TuningResolution.Refused(TuningRefusal.NoSuchService);
        }

        if (await candidates.FindSelectedAsync(networkId, serviceId, cancellationToken) is not { } selected)
        {
            return TuningResolution.Refused(TuningRefusal.NoSelectedChannel);
        }

        if (await capacity.ReadAsync(cancellationToken) is not { } reachable)
        {
            return TuningResolution.Refused(TuningRefusal.LedgerUnreadable);
        }

        TuneSystem system = selected.Tuning.System;

        if (reachable.CanServe(system))
        {
            return TuningResolution.Tunable(
                selected.Id,
                selected.Tuning,
                !reachable.Healthy.CanServe(system));
        }

        return TuningResolution.Refused(
            reachable.Undetermined.Count > 0
                ? TuningRefusal.CapacityUnknown
                : TuningRefusal.NoTunerForSystem);
    }

    public async Task<bool> CanTuneAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
        => (await ResolveTuningAsync(networkId, serviceId, cancellationToken)).CanTune;
}
