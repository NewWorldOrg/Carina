namespace Carina.Domain.Quality;

public sealed class QualityBands
{
    private readonly IReadOnlyDictionary<QualityMetric, ThresholdBand> held;

    private QualityBands(IReadOnlyDictionary<QualityMetric, ThresholdBand> held) => this.held = held;

    public bool Provisional => held.Values.Any(band => band.Provisional);

    public static QualityBands Of(IReadOnlyDictionary<QualityMetric, ThresholdBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);

        foreach (QualityMetric metric in QualityMetrics.All)
        {
            if (!bands.ContainsKey(metric))
            {
                throw new ArgumentException(
                    $"A reading of {metric} is judged against a level, so every metric this domain names carries one.",
                    nameof(bands));
            }
        }

        return new QualityBands(bands);
    }

    public ThresholdBand For(QualityMetric metric)
        => held.TryGetValue(metric, out ThresholdBand? band)
            ? band
            : throw new ArgumentOutOfRangeException(nameof(metric), metric, "A reading is held against the levels this domain names.");
}
