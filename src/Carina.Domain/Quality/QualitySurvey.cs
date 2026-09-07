namespace Carina.Domain.Quality;

public sealed record QualityRowMeasure(QualityMetric Metric, ThresholdVerdict Verdict);

public sealed record QualityRowReading(QualityLedgerRow Row, IReadOnlyList<QualityRowMeasure> Measures)
{
    public QualityStanding Standing => QualityStandings.Worst(Measures.Select(measure => measure.Verdict.Standing));

    public bool WentBeyond => QualityStandings.WentBeyond(Standing);
}

public static class QualitySurvey
{
    public static double? Reading(QualityLedgerRow row, QualityMetric metric)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!row.Counters.Measured || row.Counters.Total is not { } total)
        {
            return null;
        }

        return metric switch
        {
            QualityMetric.PacketsLost => total is 0 ? null : (double)row.Counters.Dropped!.Value / total,
            QualityMetric.PacketsLeftScrambled => total is 0 || row.ScrambledPackets is not { } left
                ? null
                : (double)left / total,
            QualityMetric.Overflows => row.Overflows,
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "A reading is one of the measures this domain names."),
        };
    }

    public static QualityObservation Observe(QualityLedgerRow row, QualityMetric metric, ThresholdBand band)
    {
        ArgumentNullException.ThrowIfNull(row);

        return QualityObservation.Of(row.Facet, ThresholdEvaluator.Judge(Reading(row, metric), band));
    }

    public static IReadOnlyList<QualityObservation> Over(
        IReadOnlyList<QualityLedgerRow> rows,
        QualityMetric metric,
        ThresholdBand band)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return [.. rows.Select(row => Observe(row, metric, band))];
    }

    public static IReadOnlyList<QualityRowReading> Read(
        IReadOnlyList<QualityLedgerRow> rows,
        IReadOnlyList<QualityMetric> metrics,
        QualityBands bands)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(bands);

        return
        [
            .. rows.Select(row => new QualityRowReading(
                row,
                [
                    .. metrics.Select(metric => new QualityRowMeasure(
                        metric,
                        ThresholdEvaluator.Judge(Reading(row, metric), bands.For(metric)))),
                ])),
        ];
    }
}
