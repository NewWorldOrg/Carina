using Carina.Domain.Base;
using Carina.Domain.Quality;

namespace Carina.Api.Services;

public sealed record QualityGroupAsk(
    DateTime? From,
    DateTime? Until,
    IReadOnlyList<QualityMetric>? Metrics,
    QualityGroupSort? Sort,
    int? Page,
    int? PerPage);

public sealed record QualityRecordingAsk(
    DateTime? From,
    DateTime? Until,
    IReadOnlyList<QualityMetric>? Metrics,
    QualityRecordingSort? Sort,
    int? Page,
    int? PerPage);

public sealed record QualitySignalStanding(QualityThresholdKey Key, QualityReading Reading, DateTime? LastTakenAt);

public sealed record QualitySummaryView(
    QualityPeriod Period,
    int Recordings,
    IReadOnlyList<QualityMeasure> Measures,
    IReadOnlyList<QualitySignalStanding> Signal,
    bool Provisional);

public sealed record QualityGroupPage(
    QualityPeriod Period,
    IReadOnlyList<QualityMetric> Metrics,
    PaginatedList<QualityGroupReading> Found,
    bool Provisional);

public sealed record QualityTunerReading(QualityGroupReading Group, IReadOnlyList<QualitySignalStanding> Signal);

public sealed record QualityTunerPage(
    QualityPeriod Period,
    IReadOnlyList<QualityMetric> Metrics,
    PaginatedList<QualityTunerReading> Found,
    bool Provisional);

public sealed record QualityRecordingPage(
    QualityPeriod Period,
    IReadOnlyList<QualityMetric> Metrics,
    PaginatedList<QualityRowReading> Found,
    IReadOnlyList<QualityMeasure> Whole,
    bool Provisional);

public sealed record QualityThresholdBook(QualityThresholdStanding Standing, QualityThresholdChange? LastChange);

public enum QualityThresholdFailure
{
    OutOfRange = 1,

    OutOfOrder = 2,
}

public static class QualitySignal
{
    public static readonly IReadOnlyList<QualityThresholdKey> Keys =
    [
        QualityThresholdKey.LockRate,
        QualityThresholdKey.CarrierToNoiseFloor,
        QualityThresholdKey.BitErrorRateCeiling,
    ];

    public static IReadOnlyList<QualitySignalStanding> NothingHasSampled(int tuners)
        => [.. Keys.Select(key => new QualitySignalStanding(key, QualityReading.Of(tuners, 0, 0), null))];
}
