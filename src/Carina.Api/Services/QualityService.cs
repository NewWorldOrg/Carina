using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Api.Services;

public sealed class QualityService(
    IQualityLedgerReader ledger,
    IQualityThresholdRepository thresholds,
    IQualitySignalReader signals,
    TimeProvider clock)
{
    public async Task<ServiceResult<QualitySummaryView>> SummariseAsync(
        DateTime? from,
        DateTime? until,
        CancellationToken cancellationToken)
    {
        if (Asked(from, until) is not { } period)
        {
            return ServiceResult<QualitySummaryView>.Failure(QualitySaying.NoSuchPeriod());
        }

        IReadOnlyList<QualityLedgerRow> rows = await ledger.ReadAsync(period, cancellationToken);
        IReadOnlyList<QualityThresholdStanding> standings = await StandingsAsync(cancellationToken);
        QualityBands bands = QualityThresholdStanding.Bands(standings);
        IReadOnlyList<SignalFigures> figures = await signals.FiguresAsync(period, cancellationToken);

        return ServiceResult<QualitySummaryView>.Success(new QualitySummaryView(
            period,
            rows.Count,
            QualityBoard.Whole(rows, QualityMetrics.All, bands),
            QualitySignal.Over(QualitySignalSurvey.Read(figures, Subjects(rows, figures), standings)),
            bands.Provisional));
    }

    public async Task<ServiceResult<QualityGroupPage>> ListChannelsAsync(
        QualityGroupAsk ask,
        CancellationToken cancellationToken)
    {
        if (Grouping(ask) is not { } query)
        {
            return ServiceResult<QualityGroupPage>.Failure(
                Refusal(ask.From, ask.Until, QualityGroupQuery.MostPerPage));
        }

        IReadOnlyList<QualityLedgerRow> rows = await ledger.ReadAsync(query.Period, cancellationToken);
        QualityBands bands = await BandsAsync(cancellationToken);

        return ServiceResult<QualityGroupPage>.Success(new QualityGroupPage(
            query.Period,
            query.Metrics,
            Paged(Grouped(rows, QualityAxis.Channel | QualityAxis.Kind, query, bands), query.Page, query.PerPage),
            bands.Provisional));
    }

    public async Task<ServiceResult<QualityTunerPage>> ListTunersAsync(
        QualityGroupAsk ask,
        CancellationToken cancellationToken)
    {
        if (Grouping(ask) is not { } query)
        {
            return ServiceResult<QualityTunerPage>.Failure(
                Refusal(ask.From, ask.Until, QualityGroupQuery.MostPerPage));
        }

        IReadOnlyList<QualityLedgerRow> rows = await ledger.ReadAsync(query.Period, cancellationToken);
        IReadOnlyList<QualityThresholdStanding> standings = await StandingsAsync(cancellationToken);
        QualityBands bands = QualityThresholdStanding.Bands(standings);
        IReadOnlyList<SignalFigures> figures = await signals.FiguresAsync(query.Period, cancellationToken);

        IReadOnlyList<QualityGroupReading> grouped = QualityBoard.Grouped(rows, QualityAxis.Tuner, query.Metrics, bands);

        IReadOnlyList<QualityTunerReading> readings =
        [
            .. QualityBoard
                .Sorted(
                    [.. grouped, .. OnlySampled(grouped, figures, query.Metrics)],
                    query.Sort,
                    query.Primary,
                    Sense(query.Primary))
                .Select(group => new QualityTunerReading(
                    group,
                    QualitySignal.Over(QualitySignalSurvey.Read(figures, Named(group), standings)))),
        ];

        return ServiceResult<QualityTunerPage>.Success(new QualityTunerPage(
            query.Period,
            query.Metrics,
            Paged(readings, query.Page, query.PerPage),
            bands.Provisional));
    }

    public async Task<ServiceResult<QualityRecordingPage>> ListRecordingsAsync(
        QualityRecordingAsk ask,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ask);

        if (Asked(ask.From, ask.Until) is not { } period
            || QualityRecordingQuery.For(period, ask.Metrics, ask.Sort, ask.Page, ask.PerPage) is not { } query)
        {
            return ServiceResult<QualityRecordingPage>.Failure(
                Refusal(ask.From, ask.Until, QualityRecordingQuery.MostPerPage));
        }

        IReadOnlyList<QualityLedgerRow> rows = await ledger.ReadAsync(query.Period, cancellationToken);
        QualityBands bands = await BandsAsync(cancellationToken);
        IReadOnlyList<QualityRowReading> read = QualitySurvey.Read(rows, query.Metrics, bands);

        IReadOnlyList<QualityRowReading> beyond = QualityBoard.Sorted(
            [.. read.Where(reading => reading.WentBeyond)],
            query.Sort,
            query.Primary,
            Sense(query.Primary));

        return ServiceResult<QualityRecordingPage>.Success(new QualityRecordingPage(
            query.Period,
            query.Metrics,
            Paged(beyond, query.Page, query.PerPage),
            QualityBoard.Whole(rows, query.Metrics, bands),
            bands.Provisional));
    }

    private static ThresholdSense Sense(QualityMetric metric)
        => QualityThresholdShapes.Of(QualityThresholdShapes.Warning(metric)).Sense;

    private static IReadOnlyList<TunerDeviceId> Named(QualityGroupReading group)
        => group.Key.Tuner is { } tuner ? [tuner] : [];

    private static IReadOnlyList<TunerDeviceId> Subjects(
        IReadOnlyList<QualityLedgerRow> rows,
        IReadOnlyList<SignalFigures> figures)
    {
        SortedSet<string> named = new(StringComparer.Ordinal);

        foreach (QualityLedgerRow row in rows)
        {
            if (row.Tuner is { } tuner)
            {
                named.Add(tuner.Value);
            }
        }

        foreach (SignalFigures figure in figures)
        {
            named.Add(figure.Tuner.Value);
        }

        return [.. named.Select(name => new TunerDeviceId(name))];
    }

    private static IReadOnlyList<QualityGroupReading> OnlySampled(
        IReadOnlyList<QualityGroupReading> grouped,
        IReadOnlyList<SignalFigures> figures,
        IReadOnlyList<QualityMetric> metrics)
    {
        HashSet<string> held =
        [
            .. grouped
                .Select(group => group.Key.Tuner?.Value)
                .OfType<string>(),
        ];

        return
        [
            .. figures
                .Where(figure => !held.Contains(figure.Tuner.Value))
                .Select(figure => new QualityGroupReading(
                    QualityGroupKey.ForTuner(figure.Tuner),
                    [.. metrics.Select(metric => new QualityMeasure(metric, QualityAggregator.Tally([])))])),
        ];
    }

    private static IReadOnlyList<QualityGroupReading> Grouped(
        IReadOnlyList<QualityLedgerRow> rows,
        QualityAxis axis,
        QualityGroupQuery query,
        QualityBands bands)
        => QualityBoard.Sorted(
            QualityBoard.Grouped(rows, axis, query.Metrics, bands),
            query.Sort,
            query.Primary,
            Sense(query.Primary));

    private static PaginatedList<T> Paged<T>(IReadOnlyList<T> found, int page, int perPage)
        => new([.. found.Skip((page - 1) * perPage).Take(perPage)], found.Count, page, perPage);

    private string Refusal(DateTime? from, DateTime? until, int mostPerPage)
        => Asked(from, until) is null ? QualitySaying.NoSuchPeriod() : QualitySaying.NoSuchPage(mostPerPage);

    private QualityGroupQuery? Grouping(QualityGroupAsk ask)
    {
        ArgumentNullException.ThrowIfNull(ask);

        return Asked(ask.From, ask.Until) is { } period
            ? QualityGroupQuery.For(period, ask.Metrics, ask.Sort, ask.Page, ask.PerPage)
            : null;
    }

    private QualityPeriod? Asked(DateTime? from, DateTime? until)
        => QualityPeriod.Of(from, until, clock.GetUtcNow().UtcDateTime);

    private async Task<IReadOnlyList<QualityThresholdStanding>> StandingsAsync(CancellationToken cancellationToken)
        => QualityThresholdStanding.Over(
            await thresholds.ListAsync(cancellationToken),
            clock.GetUtcNow().UtcDateTime);

    private async Task<QualityBands> BandsAsync(CancellationToken cancellationToken)
        => QualityThresholdStanding.Bands(await StandingsAsync(cancellationToken));
}
