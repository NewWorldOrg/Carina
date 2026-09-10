namespace Carina.Domain.Quality;

public sealed record QualityThresholdStanding(
    QualityThresholdKey Key,
    QualityThresholdShape Shape,
    Threshold Setting,
    bool Stored,
    string? UpdatedBy)
{
    public static QualityThresholdStanding AsShipped(QualityThresholdKey key, DateTime at)
        => new(key, QualityThresholdShapes.Of(key), QualityThresholdShapes.AsShipped(key, at), false, null);

    public static IReadOnlyList<QualityThresholdStanding> Over(IReadOnlyList<QualityThreshold> held, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(held);

        Dictionary<QualityThresholdKey, QualityThreshold> stored = held.ToDictionary(threshold => threshold.Key);

        return
        [
            .. QualityThresholdShapes.Consulted.Select(shape => stored.TryGetValue(shape.Key, out QualityThreshold? threshold)
                ? new QualityThresholdStanding(shape.Key, shape, threshold.Setting, true, threshold.UpdatedBy)
                : AsShipped(shape.Key, at)),
        ];
    }

    public static QualityBands Bands(IReadOnlyList<QualityThresholdStanding> standings)
    {
        ArgumentNullException.ThrowIfNull(standings);

        Dictionary<QualityThresholdKey, Threshold> levels = standings.ToDictionary(
            standing => standing.Key,
            standing => standing.Setting);

        return QualityBands.Of(QualityMetrics.All.ToDictionary(metric => metric, metric => Band(metric, levels)));
    }

    public static bool Ordered(QualityThresholdKey key, double value, IReadOnlyList<QualityThresholdStanding> standings)
    {
        ArgumentNullException.ThrowIfNull(standings);

        QualityMetric? metric = QualityThresholdShapes.Of(key).Metric;

        if (metric is not { } measured || QualityThresholdShapes.Unwatchable(measured) is not { } unwatchableKey)
        {
            return true;
        }

        QualityThresholdKey warningKey = QualityThresholdShapes.Warning(measured);
        double warning = key == warningKey ? value : Current(warningKey, standings);
        double unwatchable = key == unwatchableKey ? value : Current(unwatchableKey, standings);

        return QualityThresholdShapes.Of(warningKey).Sense is ThresholdSense.Ceiling
            ? unwatchable >= warning
            : unwatchable <= warning;
    }

    private static double Current(QualityThresholdKey key, IReadOnlyList<QualityThresholdStanding> standings)
        => standings.First(standing => standing.Key == key).Setting.Current;

    private static ThresholdBand Band(QualityMetric metric, IReadOnlyDictionary<QualityThresholdKey, Threshold> levels)
    {
        QualityThresholdKey warning = QualityThresholdShapes.Warning(metric);
        ThresholdSense sense = QualityThresholdShapes.Of(warning).Sense;

        return QualityThresholdShapes.Unwatchable(metric) is { } unwatchable
            ? ThresholdBand.Of(sense, warning, levels[warning], unwatchable, levels[unwatchable])
            : ThresholdBand.Of(sense, warning, levels[warning]);
    }
}
