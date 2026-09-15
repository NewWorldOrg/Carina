using Carina.Contracts;
using Carina.Domain.Channels;

namespace Carina.Domain.Quality;

public static class SignalFiling
{
    public static IntendedStream? StreamFor(IReadOnlyList<IntendedStream> intended, TuneParams tune)
    {
        ArgumentNullException.ThrowIfNull(tune);

        return StreamAt(intended, SlotOf(tune));
    }

    public static IntendedStream? StreamFor(IReadOnlyList<IntendedStream> intended, TuningParameters tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        return StreamAt(intended, SlotOf(tuning));
    }

    public static bool Same(TuningParameters left, TuningParameters right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return SlotOf(left) == SlotOf(right);
    }

    private static IntendedStream? StreamAt(IReadOnlyList<IntendedStream> intended, Slot slot)
    {
        ArgumentNullException.ThrowIfNull(intended);

        foreach (IntendedStream stream in intended)
        {
            if (stream.Services.Count > 0 && SlotOf(stream.Tuning) == slot)
            {
                return stream;
            }
        }

        return null;
    }

    private static Slot SlotOf(TuningParameters tuning)
        => new(
            tuning.System,
            tuning.PhysicalChannel,
            tuning.System is TuneSystem.IsdbSBs ? tuning.TransportStreamId?.Value : null);

    private static Slot SlotOf(TuneParams tune)
    {
        int? channel = tune.System switch
        {
            TuneSystem.IsdbT => tune.IsdbT?.PhysicalChannel,
            TuneSystem.IsdbSBs => tune.IsdbSBs?.BsChannel,
            TuneSystem.IsdbSCs110 => tune.IsdbSCs110?.CsChannel,
            _ => null,
        };

        int? carried = tune.System is TuneSystem.IsdbSBs ? tune.IsdbSBs?.Tsid : null;

        return new Slot(tune.System, channel, carried);
    }

    private readonly record struct Slot(TuneSystem System, int? Channel, int? SatelliteStream);
}
