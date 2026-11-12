using Carina.Domain.Channels;

using Puppis.Contracts;

namespace Carina.Domain.Scans;

/// <summary>
/// Decides what a scan walks, looking up the tuners in service and the satellite streams known: only the systems a
/// tuner in service receives, or every one asked for while what a tuner receives is undetermined; every terrestrial
/// channel, every known BS stream, and every other CS slot.
/// </summary>
public sealed class ScanPlanningDomainService(
    ITunerCapacityDirectory capacity,
    ISatelliteTransportStreamRepository satelliteStreams)
{
    /// <summary>
    /// What the scope walks, or nothing when no tuner in service receives any of it.
    /// </summary>
    public async Task<ScanPlan?> PlanAsync(ScanScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (await ReceivableAsync(scope, cancellationToken) is not { } walkable)
        {
            return null;
        }

        return new ScanPlan(walkable, await TargetsAsync(walkable, cancellationToken));
    }

    private async Task<ScanScope?> ReceivableAsync(ScanScope scope, CancellationToken cancellationToken)
    {
        if (await capacity.ReadAsync(cancellationToken) is not { Undetermined.Count: 0 } known)
        {
            return scope;
        }

        return scope.Within(known.Reachable);
    }

    private async Task<IReadOnlyList<TuningParameters>> TargetsAsync(ScanScope scope, CancellationToken cancellationToken)
    {
        if (scope.NamesItsOwnTargets)
        {
            return scope.NamedTargets;
        }

        List<TuningParameters> targets = [];

        if (scope.Covers(TuneSystem.IsdbT))
        {
            for (int channel = BroadcastStandards.TerrestrialFirstChannel;
                 channel <= BroadcastStandards.TerrestrialLastChannel;
                 channel++)
            {
                targets.Add(TuningParameters.Terrestrial(channel));
            }
        }

        if (scope.Covers(TuneSystem.IsdbSBs))
        {
            IReadOnlyList<SatelliteTransportStream> known = await satelliteStreams.ListAsync(cancellationToken);

            targets.AddRange(known
                .OrderBy(stream => stream.BsChannel)
                .ThenBy(stream => stream.RelativeStreamNumber)
                .Select(stream => stream.ToTuningParameters()));
        }

        if (scope.Covers(TuneSystem.IsdbSCs110))
        {
            for (int slot = BroadcastStandards.Cs110FirstChannel;
                 slot <= BroadcastStandards.Cs110LastChannel;
                 slot += 2)
            {
                targets.Add(TuningParameters.Cs110(slot));
            }
        }

        return targets;
    }
}
