using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Scans;

namespace Carina.Infrastructure.Scanning;

public static class ScanTargets
{
    public static async Task<IReadOnlyList<TuningParameters>> WalkAsync(
        ScanScope scope,
        ISatelliteTransportStreamRepository satelliteStreams,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(satelliteStreams);

        if (scope.NamesItsOwnTargets)
        {
            return scope.NamedTargets;
        }

        var targets = new List<TuningParameters>();

        if (scope.Covers(TuneSystem.IsdbT))
        {
            for (var channel = BroadcastStandards.TerrestrialFirstChannel;
                 channel <= BroadcastStandards.TerrestrialLastChannel;
                 channel++)
            {
                targets.Add(TuningParameters.Terrestrial(channel));
            }
        }

        if (scope.Covers(TuneSystem.IsdbSBs))
        {
            var known = await satelliteStreams.ListAsync(cancellationToken);

            targets.AddRange(known
                .OrderBy(stream => stream.BsChannel)
                .ThenBy(stream => stream.RelativeStreamNumber)
                .Select(stream => stream.ToTuningParameters()));
        }

        if (scope.Covers(TuneSystem.IsdbSCs110))
        {
            for (var slot = BroadcastStandards.Cs110FirstChannel;
                 slot <= BroadcastStandards.Cs110LastChannel;
                 slot += 2)
            {
                targets.Add(TuningParameters.Cs110(slot));
            }
        }

        return targets;
    }
}
