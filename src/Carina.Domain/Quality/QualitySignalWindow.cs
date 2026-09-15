using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Quality;

public sealed record LayerErrorPeak(int Layer, double Highest);

public sealed record QualitySignalWindow(
    DateTime Start,
    TunerDeviceId Tuner,
    NetworkId Network,
    ServiceId Service,
    long Samples,
    long Locked,
    long Unmeasured,
    long Unreachable,
    int? CarrierToNoiseLowest,
    IReadOnlyList<LayerErrorPeak> BitErrors,
    IReadOnlyList<string> MetricsNotRead,
    DateTime? LastCarriedAt)
{
    public static QualitySignalWindow Of(QualitySignalRollup rollup)
    {
        ArgumentNullException.ThrowIfNull(rollup);

        return new QualitySignalWindow(
            rollup.WindowStart,
            rollup.Tuner,
            rollup.Network,
            rollup.Service,
            rollup.Samples,
            rollup.Locked,
            rollup.Unmeasured,
            rollup.Unreachable,
            rollup.CarrierToNoiseLowest,
            [.. rollup.BitErrors.Select(rate => new LayerErrorPeak(rate.Layer, rate.Highest))],
            [],
            rollup.CarrierToNoiseLowest is not null || rollup.BitErrors.Count > 0 ? rollup.WindowStart : null);
    }

    public static QualitySignalWindow Of(QualitySignalSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        SignalSample signal = sample.Signal;

        return new QualitySignalWindow(
            sample.TakenAt,
            sample.Tuner,
            sample.Network,
            sample.Service,
            1,
            signal.Locked ? 1 : 0,
            signal.WasTaken && !signal.CarriesAnyValue ? 1 : 0,
            signal.WasTaken ? 0 : 1,
            signal.CarrierToNoiseMilliDecibels,
            [
                .. signal.BitErrors
                    .Where(counts => counts.ErrorRate.HasValue)
                    .Select(counts => new LayerErrorPeak(counts.Layer, counts.ErrorRate.GetValueOrDefault())),
            ],
            signal.MetricsNotRead,
            signal.CarriesAnyValue ? sample.TakenAt : null);
    }
}
