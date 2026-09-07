namespace Carina.Domain.Quality;

public enum QualityMetric
{
    PacketsLost = 1,

    PacketsLeftScrambled = 2,

    Overflows = 3,
}

public static class QualityMetrics
{
    public static readonly IReadOnlyList<QualityMetric> All =
    [
        QualityMetric.PacketsLost,
        QualityMetric.PacketsLeftScrambled,
        QualityMetric.Overflows,
    ];

    public static IReadOnlyList<QualityMetric>? Named(IReadOnlyList<QualityMetric>? asked)
    {
        if (asked is null || asked.Count is 0)
        {
            return All;
        }

        QualityMetric[] apart = [.. asked.Distinct()];

        return apart.Any(metric => !Enum.IsDefined(metric)) ? null : [.. All.Where(apart.Contains)];
    }
}
