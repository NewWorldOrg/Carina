namespace Carina.Domain.Quality;

public sealed record QualityThresholdShape(
    QualityThresholdKey Key,
    QualityMetric? Metric,
    ThresholdSense Sense,
    double Lowest,
    double Highest,
    double Shipped)
{
    public bool Holds(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value) && value >= Lowest && value <= Highest;
}

public static class QualityThresholdShapes
{
    private const double WholeOfIt = 1;

    private const double MostOverflowsCountable = 1_000_000;

    private const double ColdestCarrierToNoiseInMilliDecibels = -100_000;

    private const double WarmestCarrierToNoiseInMilliDecibels = 100_000;

    private const double LongestSilenceInSeconds = 86_400;

    public static readonly IReadOnlyList<QualityThresholdShape> All =
    [
        new(QualityThresholdKey.PacketsLostWarning, QualityMetric.PacketsLost, ThresholdSense.Ceiling, 0, WholeOfIt, 0.0002),
        new(QualityThresholdKey.PacketsLostUnwatchable, QualityMetric.PacketsLost, ThresholdSense.Ceiling, 0, WholeOfIt, 0.001),
        new(QualityThresholdKey.PacketsLeftScrambled, QualityMetric.PacketsLeftScrambled, ThresholdSense.Ceiling, 0, WholeOfIt, 0.0005),
        new(QualityThresholdKey.Overflows, QualityMetric.Overflows, ThresholdSense.Ceiling, 0, MostOverflowsCountable, 1),
        new(QualityThresholdKey.LockRate, null, ThresholdSense.Floor, 0, WholeOfIt, 0.99),
        new(
            QualityThresholdKey.CarrierToNoiseFloor,
            null,
            ThresholdSense.Floor,
            ColdestCarrierToNoiseInMilliDecibels,
            WarmestCarrierToNoiseInMilliDecibels,
            15_000),
        new(QualityThresholdKey.BitErrorRateCeiling, null, ThresholdSense.Ceiling, 0, WholeOfIt, 0.0001),
        new(QualityThresholdKey.SupplySilence, null, ThresholdSense.Ceiling, 1, LongestSilenceInSeconds, 300),
    ];

    public static QualityThresholdShape Of(QualityThresholdKey key)
        => All.FirstOrDefault(shape => shape.Key == key)
           ?? throw new ArgumentOutOfRangeException(nameof(key), key, "A threshold is kept under one of the keys this domain names.");

    public static QualityThresholdKey Warning(QualityMetric metric) => metric switch
    {
        QualityMetric.PacketsLost => QualityThresholdKey.PacketsLostWarning,
        QualityMetric.PacketsLeftScrambled => QualityThresholdKey.PacketsLeftScrambled,
        QualityMetric.Overflows => QualityThresholdKey.Overflows,
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "A reading is held against the levels this domain names."),
    };

    public static QualityThresholdKey? Unwatchable(QualityMetric metric)
        => metric is QualityMetric.PacketsLost ? QualityThresholdKey.PacketsLostUnwatchable : null;

    public static Threshold AsShipped(QualityThresholdKey key, DateTime at)
        => Threshold.Provisionally(Of(key).Shipped, 0, at);
}
