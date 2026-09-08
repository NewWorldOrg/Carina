using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Quality;

public sealed record QualityRollupSpan(DateTime From, DateTime Until);

public static class QualitySignalRollupPlan
{
    public static QualityRollupSpan? Span(
        DateTime now,
        QualityWindow granularity,
        DateTime? latestWindowStart,
        TimeSpan reach)
    {
        DateTime until = QualityWindows.StartOf(now, granularity);

        DateTime from = latestWindowStart is { } rolled
            ? QualityWindows.EndOf(rolled, granularity)
            : QualityWindows.StartOf(now - reach, granularity);

        return from < until ? new QualityRollupSpan(from, until) : null;
    }

    public static IReadOnlyList<QualitySignalRollup> Over(
        IReadOnlyList<QualitySignalSample> samples,
        QualityWindow granularity)
    {
        ArgumentNullException.ThrowIfNull(samples);

        return
        [
            .. samples
                .GroupBy(sample => new Bucket(
                    QualityWindows.StartOf(sample.TakenAt, granularity),
                    sample.Tuner.Value,
                    sample.Network.Value,
                    sample.Service.Value))
                .Select(group => Rolled(group.Key, granularity, [.. group]))
                .OrderBy(rollup => rollup.WindowStart)
                .ThenBy(rollup => rollup.Tuner.Value, StringComparer.Ordinal)
                .ThenBy(rollup => rollup.Network.Value)
                .ThenBy(rollup => rollup.Service.Value),
        ];
    }

    private static QualitySignalRollup Rolled(
        Bucket bucket,
        QualityWindow granularity,
        IReadOnlyList<QualitySignalSample> taken)
    {
        long locked = taken.Count(sample => sample.Signal.Locked);
        long unreachable = taken.Count(sample => !sample.Signal.WasTaken);
        long unmeasured = taken.Count(sample => sample.Signal.WasTaken && !sample.Signal.CarriesAnyValue);

        int[] figures = [.. taken.Select(sample => sample.Signal.CarrierToNoiseMilliDecibels).OfType<int>()];

        return QualitySignalRollup.Rehydrate(
            granularity,
            bucket.WindowStart,
            new TunerDeviceId(bucket.Tuner),
            new NetworkId(bucket.Network),
            new ServiceId(bucket.Service),
            taken.Count,
            locked,
            unmeasured,
            unreachable,
            figures.Length is 0 ? null : figures.Average(),
            figures.Length is 0 ? null : figures.Min(),
            figures.Length is 0 ? null : figures.Max(),
            Layers(taken));
    }

    private static IReadOnlyList<LayerErrorRate> Layers(IReadOnlyList<QualitySignalSample> taken)
    {
        Dictionary<int, List<double>> rates = [];

        foreach (QualitySignalSample sample in taken)
        {
            foreach (LayerBitErrorCounts counts in sample.Signal.BitErrors)
            {
                if (counts.ErrorRate is not { } rate)
                {
                    continue;
                }

                if (!rates.TryGetValue(counts.Layer, out List<double>? held))
                {
                    held = [];
                    rates[counts.Layer] = held;
                }

                held.Add(rate);
            }
        }

        return
        [
            .. rates
                .OrderBy(layer => layer.Key)
                .Select(layer => new LayerErrorRate(layer.Key, layer.Value.Average(), layer.Value.Max())),
        ];
    }

    private readonly record struct Bucket(DateTime WindowStart, string Tuner, int Network, int Service);
}
