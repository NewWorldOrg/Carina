using Carina.Contracts;
using Carina.Domain.Channels;

namespace Carina.Infrastructure.Scanning;

public static class ScanTargetNames
{
    public static string Of(TuningParameters tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        return tuning.System switch
        {
            TuneSystem.IsdbT => $"terrestrial channel {tuning.PhysicalChannel}",
            TuneSystem.IsdbSBs =>
                $"BS slot {tuning.PhysicalChannel} stream {tuning.TransportStreamId?.Value}",
            TuneSystem.IsdbSCs110 => $"CS slot {tuning.PhysicalChannel}",
            _ => "an unnameable channel",
        };
    }
}
