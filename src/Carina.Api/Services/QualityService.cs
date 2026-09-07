using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Quality;

namespace Carina.Api.Services;

public sealed class QualityService(
    IQualityLedgerReader ledger,
    IQualityThresholdRepository thresholds,
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
        QualityBands bands = await BandsAsync(cancellationToken);

        return ServiceResult<QualitySummaryView>.Success(new QualitySummaryView(
            period,
            rows.Count,
            QualityBoard.Whole(rows, QualityMetrics.All, bands),
            QualitySignal.NothingHasSampled(Tuners(rows).Count),
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
        QualityBands bands = await BandsAsync(cancellationToken);

        IReadOnlyList<QualityTunerReading> readings =
        [
            .. Grouped(rows, QualityAxis.Tuner, query, bands)
                .Select(group => new QualityTunerReading(
                    group,
                    QualitySignal.NothingHasSampled(group.Key.Tuner is null ? 0 : 1))),
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

    private static IReadOnlySet<string> Tuners(IReadOnlyList<QualityLedgerRow> rows)
        => rows.Where(row => row.Tuner is not null).Select(row => row.Tuner!.Value).ToHashSet(StringComparer.Ordinal);

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

    private async Task<QualityBands> BandsAsync(CancellationToken cancellationToken)
        => QualityThresholdStanding.Bands(QualityThresholdStanding.Over(
            await thresholds.ListAsync(cancellationToken),
            clock.GetUtcNow().UtcDateTime));
}
