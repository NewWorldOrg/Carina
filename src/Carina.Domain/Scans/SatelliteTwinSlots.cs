using System.Diagnostics.CodeAnalysis;

using Carina.Domain.Channels;

using Puppis.Contracts;

namespace Carina.Domain.Scans;

public sealed class SatelliteTwinSlots
{
    private readonly Dictionary<(int Network, int Stream), TuningParameters> seen = [];

    public bool TryFindTwin(
        TuningParameters target,
        int originalNetworkId,
        int transportStreamId,
        [NotNullWhen(true)] out TuningParameters? first)
    {
        ArgumentNullException.ThrowIfNull(target);

        (int, int) stream = (originalNetworkId, transportStreamId);

        if (target.System is TuneSystem.IsdbSBs && seen.TryGetValue(stream, out first))
        {
            return true;
        }

        seen[stream] = target;
        first = null;

        return false;
    }
}
