using Carina.Api.Services;

using Carina.Contracts;
using Carina.Domain.Quality;

namespace Carina.Api.Responder.Quality;

public sealed record QualityVerdictResponder(
    QualityMetric Metric,
    QualityStanding Standing,
    double? Observed,
    QualityThresholdKey Applied,
    double AppliedValue,
    bool Provisional,
    QualityThresholdKey? Breached)
{
    public static QualityVerdictResponder Of(QualityRowMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(measure);

        return new QualityVerdictResponder(
            measure.Metric,
            measure.Verdict.Standing,
            measure.Verdict.Observed,
            measure.Verdict.AppliedKey,
            measure.Verdict.Applied.Current,
            measure.Verdict.Provisional,
            measure.Verdict.Breached);
    }
}

public sealed record QualityRecordingResponder(
    string Id,
    int NetworkId,
    int ServiceId,
    TuneSystem? Kind,
    string? TunerDeviceId,
    DateTime StartedAt,
    DateTime? MeasuredUpdatedAt,
    QualityStanding Standing,
    long? DroppedPackets,
    long? TotalPackets,
    long? ScrambledPackets,
    long Overflows,
    IReadOnlyList<QualityVerdictResponder> Verdicts)
{
    public static QualityRecordingResponder Of(QualityRowReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        QualityLedgerRow row = reading.Row;

        return new QualityRecordingResponder(
            row.Recording.Value.ToString(),
            row.Network.Value,
            row.Service.Value,
            row.Kind,
            row.Tuner?.Value,
            row.StartedAt,
            row.MeasuredUpdatedAt,
            reading.Standing,
            row.Counters.Dropped,
            row.Counters.Total,
            row.ScrambledPackets,
            row.Overflows,
            [.. reading.Measures.Select(QualityVerdictResponder.Of)]);
    }
}

public sealed record QualityRecordingListResponder(
    QualityPeriodResponder Period,
    IReadOnlyList<QualityMetric> Metrics,
    IReadOnlyList<QualityRecordingResponder> Items,
    int Total,
    int CurrentPage,
    int LastPage,
    int PerPage,
    IReadOnlyList<QualityMeasureResponder> Whole,
    bool Provisional)
{
    public static QualityRecordingListResponder Of(QualityRecordingPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new QualityRecordingListResponder(
            QualityPeriodResponder.Of(page.Period),
            page.Metrics,
            [.. page.Found.Items.Select(QualityRecordingResponder.Of)],
            page.Found.Total,
            page.Found.CurrentPage,
            page.Found.LastPage,
            page.Found.PerPage,
            QualityMeasureResponder.Over(page.Whole),
            page.Provisional);
    }
}
