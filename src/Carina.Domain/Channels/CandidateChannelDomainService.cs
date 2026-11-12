using Carina.Domain.Driver;

using Puppis.Contracts;

namespace Carina.Domain.Channels;

/// <summary>
/// A channel added to a service by hand, or why it is not: the service holds it already, the driver could not be asked
/// which tuners it holds, or none of them in service is of the kind that receives it, with the kind needed and the kinds
/// in service.
/// </summary>
public sealed record CandidateChannelAdmission(
    CandidateChannel? Added,
    CandidateChannelRefusal? Refusal,
    TunerKind Needed,
    IReadOnlyList<TunerKind> InService);

/// <summary>
/// Decides whether a channel may be added to a service as another candidate, looking up the candidates the service
/// holds and the tuners the driver holds.
/// </summary>
public sealed class CandidateChannelDomainService(
    ICandidateChannelRepository candidates,
    IDriverClient driver,
    TimeProvider clock)
{
    public async Task<CandidateChannelAdmission> AdmitAsync(
        NetworkId networkId,
        ServiceId serviceId,
        TuningParameters tuning,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(tuning);

        TunerKind needed = BroadcastReception.KindReceiving(tuning.System);
        IReadOnlyList<CandidateChannel> held = await candidates.ListForServiceAsync(networkId, serviceId, cancellationToken);

        if (held.Any(candidate => candidate.Tuning.Equals(tuning)))
        {
            return Refused(CandidateChannelRefusal.AlreadyHeld, needed, []);
        }

        DriverCall<IReadOnlyList<TunerSnapshot>> tuners = await driver.GetTunersAsync(cancellationToken);

        if (!tuners.TryGetValue(out IReadOnlyList<TunerSnapshot>? snapshots))
        {
            return Refused(CandidateChannelRefusal.DriverUnreachable, needed, []);
        }

        TunerKind[] inService =
        [
            .. snapshots
                .Where(tuner => tuner.State is not (TunerState.Disabled or TunerState.Faulted))
                .Select(tuner => tuner.Kind)
                .Distinct(),
        ];

        if (!inService.Contains(needed))
        {
            return Refused(CandidateChannelRefusal.NoTunerReceivesIt, needed, inService);
        }

        CandidateChannel added = CandidateChannel.AddByHand(
            CandidateChannelId.New(),
            networkId,
            serviceId,
            tuning,
            clock.GetUtcNow().UtcDateTime);

        return new CandidateChannelAdmission(added, null, needed, inService);
    }

    private static CandidateChannelAdmission Refused(
        CandidateChannelRefusal refusal,
        TunerKind needed,
        IReadOnlyList<TunerKind> inService)
        => new(null, refusal, needed, inService);
}
