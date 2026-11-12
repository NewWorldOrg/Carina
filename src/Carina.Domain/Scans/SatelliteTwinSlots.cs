using System.Diagnostics.CodeAnalysis;

using Carina.Domain.Channels;

using Puppis.Contracts;

namespace Carina.Domain.Scans;

/// <summary>
/// The streams a scan has seen so far, so that a BS slot carrying a stream an earlier slot carried is proposed once.
/// Only satellite slots are folded: a terrestrial stream heard on two channels is a relay, another way to the same
/// services.
/// </summary>
public sealed class SatelliteTwinSlots
{
    private readonly Dictionary<(int Network, int Stream), TuningParameters> seen = [];

    /// <summary>
    /// Whether this is a BS slot carrying a stream another slot carried first, naming that slot; when it is not, this
    /// slot is remembered as carrying the stream.
    /// </summary>
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
