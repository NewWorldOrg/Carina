using Carina.Domain.Base;

namespace Carina.Domain.Quality;

public sealed class QualityGroupQuery
{
    public const int MostPerPage = 200;

    public const int DefaultPerPage = 50;

    private QualityGroupQuery(
        QualityPeriod period,
        IReadOnlyList<QualityMetric> metrics,
        QualityGroupSort sort,
        int page,
        int perPage)
    {
        Period = period;
        Metrics = metrics;
        Sort = sort;
        Page = page;
        PerPage = perPage;
    }

    public QualityPeriod Period { get; }

    public IReadOnlyList<QualityMetric> Metrics { get; }

    public QualityGroupSort Sort { get; }

    public int Page { get; }

    public int PerPage { get; }

    public QualityMetric Primary => Metrics[0];

    public static QualityGroupQuery? For(
        QualityPeriod period,
        IReadOnlyList<QualityMetric>? metrics,
        QualityGroupSort? sort,
        int? page,
        int? perPage)
    {
        ArgumentNullException.ThrowIfNull(period);

        if (QualityMetrics.Named(metrics) is not { } asked || page is < 1)
        {
            return null;
        }

        if (sort is { } ordering && !Enum.IsDefined(ordering))
        {
            return null;
        }

        return new QualityGroupQuery(
            period,
            asked,
            sort ?? QualityGroupSort.Worst,
            page ?? 1,
            ListingGuards.Clamped(perPage, DefaultPerPage, MostPerPage));
    }
}

public sealed class QualityRecordingQuery
{
    public const int MostPerPage = QualityGroupQuery.MostPerPage;

    public const int DefaultPerPage = QualityGroupQuery.DefaultPerPage;

    private QualityRecordingQuery(
        QualityPeriod period,
        IReadOnlyList<QualityMetric> metrics,
        QualityRecordingSort sort,
        int page,
        int perPage)
    {
        Period = period;
        Metrics = metrics;
        Sort = sort;
        Page = page;
        PerPage = perPage;
    }

    public QualityPeriod Period { get; }

    public IReadOnlyList<QualityMetric> Metrics { get; }

    public QualityRecordingSort Sort { get; }

    public int Page { get; }

    public int PerPage { get; }

    public QualityMetric Primary => Metrics[0];

    public static QualityRecordingQuery? For(
        QualityPeriod period,
        IReadOnlyList<QualityMetric>? metrics,
        QualityRecordingSort? sort,
        int? page,
        int? perPage)
    {
        ArgumentNullException.ThrowIfNull(period);

        if (QualityMetrics.Named(metrics) is not { } asked || page is < 1)
        {
            return null;
        }

        if (sort is { } ordering && !Enum.IsDefined(ordering))
        {
            return null;
        }

        return new QualityRecordingQuery(
            period,
            asked,
            sort ?? QualityRecordingSort.Worst,
            page ?? 1,
            ListingGuards.Clamped(perPage, DefaultPerPage, MostPerPage));
    }
}
