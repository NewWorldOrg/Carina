namespace Carina.Domain.Quality;

public enum QualityGroupSort
{
    Worst = 1,

    Unmeasured = 2,

    Subjects = 3,

    Identity = 4,
}

public enum QualityRecordingSort
{
    Worst = 1,

    StartedAt = 2,
}

public sealed record QualityMeasure(QualityMetric Metric, QualityTally Tally);

public sealed record QualityGroupReading(QualityGroupKey Key, IReadOnlyList<QualityMeasure> Measures)
{
    public int Subjects => Measures.Count is 0 ? 0 : Measures[0].Tally.Subjects;

    public QualityTally Of(QualityMetric metric)
        => Measures.FirstOrDefault(measure => measure.Metric == metric)?.Tally
           ?? throw new ArgumentOutOfRangeException(nameof(metric), metric, "This reading was not asked for that measure.");
}

public static class QualityBoard
{
    public static IReadOnlyList<QualityMeasure> Whole(
        IReadOnlyList<QualityLedgerRow> rows,
        IReadOnlyList<QualityMetric> metrics,
        QualityBands bands)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(bands);

        return
        [
            .. metrics.Select(metric => new QualityMeasure(
                metric,
                QualityAggregator.Tally(QualitySurvey.Over(rows, metric, bands.For(metric))))),
        ];
    }

    public static IReadOnlyList<QualityGroupReading> Grouped(
        IReadOnlyList<QualityLedgerRow> rows,
        QualityAxis axis,
        IReadOnlyList<QualityMetric> metrics,
        QualityBands bands)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(bands);

        Dictionary<QualityMetric, Dictionary<QualityGroupKey, QualityTally>> byMetric = metrics.ToDictionary(
            metric => metric,
            metric => QualityAggregator
                .GroupBy(QualitySurvey.Over(rows, metric, bands.For(metric)), axis)
                .ToDictionary(grouping => grouping.Key, grouping => grouping.Tally));

        IReadOnlyList<QualityGroupKey> keys = metrics.Count is 0
            ? []
            : [.. byMetric[metrics[0]].Keys];

        return
        [
            .. keys.Select(key => new QualityGroupReading(
                key,
                [.. metrics.Select(metric => new QualityMeasure(metric, byMetric[metric][key]))])),
        ];
    }

    public static IReadOnlyList<QualityGroupReading> Sorted(
        IReadOnlyList<QualityGroupReading> readings,
        QualityGroupSort sort,
        QualityMetric primary,
        ThresholdSense sense)
    {
        ArgumentNullException.ThrowIfNull(readings);
        ThresholdBand.Named(sense);

        int worstFirst = sense is ThresholdSense.Ceiling ? -1 : 1;

        return sort switch
        {
            QualityGroupSort.Worst =>
            [
                .. readings
                    .OrderBy(reading => reading.Of(primary).Worst(sense) is null)
                    .ThenBy(reading => (reading.Of(primary).Worst(sense) ?? 0) * worstFirst)
                    .ThenByDescending(reading => reading.Of(primary).BeyondThreshold)
                    .ThenBy(reading => reading.Key, QualityGroupKeyOrder.Instance),
            ],
            QualityGroupSort.Unmeasured =>
            [
                .. readings
                    .OrderByDescending(reading => reading.Of(primary).Unmeasured)
                    .ThenBy(reading => reading.Key, QualityGroupKeyOrder.Instance),
            ],
            QualityGroupSort.Subjects =>
            [
                .. readings
                    .OrderByDescending(reading => reading.Subjects)
                    .ThenBy(reading => reading.Key, QualityGroupKeyOrder.Instance),
            ],
            QualityGroupSort.Identity => [.. readings.OrderBy(reading => reading.Key, QualityGroupKeyOrder.Instance)],
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "A list is ordered by one of the ways this domain names."),
        };
    }

    public static IReadOnlyList<QualityRowReading> Sorted(
        IReadOnlyList<QualityRowReading> readings,
        QualityRecordingSort sort,
        QualityMetric primary,
        ThresholdSense sense)
    {
        ArgumentNullException.ThrowIfNull(readings);
        ThresholdBand.Named(sense);

        int worstFirst = sense is ThresholdSense.Ceiling ? -1 : 1;

        return sort switch
        {
            QualityRecordingSort.Worst =>
            [
                .. readings
                    .OrderByDescending(reading => QualityStandings.WentBeyond(reading.Standing))
                    .ThenBy(reading => Observed(reading, primary) is null)
                    .ThenBy(reading => (Observed(reading, primary) ?? 0) * worstFirst)
                    .ThenByDescending(reading => reading.Row.StartedAt)
                    .ThenBy(reading => reading.Row.Recording.Value),
            ],
            QualityRecordingSort.StartedAt =>
            [
                .. readings
                    .OrderByDescending(reading => reading.Row.StartedAt)
                    .ThenBy(reading => reading.Row.Recording.Value),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "A list is ordered by one of the ways this domain names."),
        };
    }

    private static double? Observed(QualityRowReading reading, QualityMetric metric)
        => reading.Measures.FirstOrDefault(measure => measure.Metric == metric)?.Verdict.Observed;
}
