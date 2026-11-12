using Puppis.Contracts;

namespace Carina.Domain.Channels;

/// <summary>
/// Decides where a service is tuned, looking up the service, its selected channel and the tuners in service: it is
/// refused when the service or its selected channel is missing, when the tuners cannot be read, or when no tuner of
/// the channel's system is in service, and it is tuned as impaired when only a faulted tuner can take it.
/// </summary>
public sealed class ServiceTuningDomainService(
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
            return TuningResolution.Refused(TuningRefusal.LedgerUnreadable, selected.Tuning);
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
                : TuningRefusal.NoTunerForSystem,
            selected.Tuning);
    }

    public async Task<bool> CanTuneAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
        => (await ResolveTuningAsync(networkId, serviceId, cancellationToken)).CanTune;
}
