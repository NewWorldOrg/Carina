using Carina.Domain.Channels;

namespace Carina.Domain.Quality;

public sealed record QualityTrendPoint(
    DateTime From,
    DateTime Until,
    QualityReading Reading,
    double? Worst,
    double Level,
    IReadOnlyList<LayerErrorPeak> Layers)
{
    public QualityState State => QualityStates.Of(Reading);
}

public sealed record QualityTrendChannel(NetworkId Network, ServiceId Service);

public sealed record QualityTrendSeries(QualityTrendChannel? Channel, IReadOnlyList<QualityTrendPoint> Points);

public static class QualityTrend
{
    public static QualityTrendSeries Recordings(
        QualityTrendFrame frame,
        QualityMetric metric,
        IReadOnlyList<QualityLedgerRow> rows,
        QualityThresholdHistory levels)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(levels);

        List<QualityLedgerRow>[] placed = Placed(frame, rows, row => row.StartedAt);

        return new QualityTrendSeries(
            null,
            [
                .. frame.Buckets.Select((bucket, index) =>
                {
                    ThresholdBand band = QualityThresholdStanding.Bands(levels.At(bucket.Until)).For(metric);
                    QualityTally tally = QualityAggregator.Tally(QualitySurvey.Over(placed[index], metric, band));

                    return new QualityTrendPoint(
                        bucket.From,
                        bucket.Until,
                        tally.Reading,
                        tally.Worst(band.Sense),
                        band.Warning.Current,
                        []);
                }),
            ]);
    }

    public static IReadOnlyList<QualityTrendSeries> Signal(
        QualityTrendFrame frame,
        QualityThresholdKey key,
        IReadOnlyList<QualitySignalWindow> windows,
        QualityThresholdHistory levels)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(levels);

        if (!QualitySignalSurvey.Keys.Contains(key))
        {
            throw new ArgumentOutOfRangeException(
                nameof(key),
                key,
                "A signal trend follows one of the readings this domain takes from the signal.");
        }

        return
        [
            Series(frame, key, null, windows, levels),
            .. windows
                .GroupBy(window => (Network: window.Network.Value, Service: window.Service.Value))
                .OrderBy(group => group.Key.Network)
                .ThenBy(group => group.Key.Service)
                .Select(group => Series(
                    frame,
                    key,
                    new QualityTrendChannel(new NetworkId(group.Key.Network), new ServiceId(group.Key.Service)),
                    [.. group],
                    levels)),
        ];
    }

    private static QualityTrendSeries Series(
        QualityTrendFrame frame,
        QualityThresholdKey key,
        QualityTrendChannel? channel,
        IReadOnlyList<QualitySignalWindow> windows,
        QualityThresholdHistory levels)
    {
        List<QualitySignalWindow>[] placed = Placed(frame, windows, window => window.Start);

        return new QualityTrendSeries(
            channel,
            [.. frame.Buckets.Select((bucket, index) => Point(bucket, key, placed[index], levels.At(bucket.Until)))]);
    }

    private static QualityTrendPoint Point(
        QualityTrendBucket bucket,
        QualityThresholdKey key,
        IReadOnlyList<QualitySignalWindow> windows,
        IReadOnlyList<QualityThresholdStanding> standings)
    {
        IReadOnlyList<SignalFigures> figures = QualitySignalSurvey.Figures(windows);
        QualitySignalRead read = QualitySignalSurvey
            .Read(figures, [.. figures.Select(figure => figure.Tuner)], standings)
            .First(one => one.Key == key);

        return new QualityTrendPoint(
            bucket.From,
            bucket.Until,
            read.Reading,
            Worst(key, figures),
            standings.First(standing => standing.Key == key).Setting.Current,
            key is QualityThresholdKey.BitErrorRateCeiling ? Layers(windows) : []);
    }

    private static double? Worst(QualityThresholdKey key, IReadOnlyList<SignalFigures> figures)
    {
        bool ceiling = QualityThresholdShapes.Of(key).Sense is ThresholdSense.Ceiling;
        double? worst = null;

        foreach (SignalFigures figure in figures)
        {
            if (QualitySignalSurvey.Observed(key, figure) is not { } observed)
            {
                continue;
            }

            if (worst is not { } held || (ceiling ? observed > held : observed < held))
            {
                worst = observed;
            }
        }

        return worst;
    }

    private static IReadOnlyList<LayerErrorPeak> Layers(IReadOnlyList<QualitySignalWindow> windows)
        =>
        [
            .. windows
                .SelectMany(window => window.BitErrors)
                .GroupBy(peak => peak.Layer)
                .OrderBy(layer => layer.Key)
                .Select(layer => new LayerErrorPeak(layer.Key, layer.Max(peak => peak.Highest))),
        ];

    private static List<T>[] Placed<T>(QualityTrendFrame frame, IEnumerable<T> items, Func<T, DateTime> at)
    {
        List<T>[] placed = Enumerable.Range(0, frame.Buckets.Count).Select(index => new List<T>()).ToArray();

        foreach (T item in items)
        {
            if (frame.IndexOf(at(item)) is >= 0 and int index)
            {
                placed[index].Add(item);
            }
        }

        return placed;
    }
}
