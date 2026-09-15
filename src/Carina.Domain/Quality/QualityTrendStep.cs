namespace Carina.Domain.Quality;

public enum QualityTrendStep
{
    Hour = 1,

    ThreeHours = 2,

    SixHours = 3,

    TwelveHours = 4,

    Day = 5,

    TwoDays = 6,
}

public static class QualityTrendSteps
{
    public static readonly IReadOnlyList<QualityTrendStep> All =
    [
        QualityTrendStep.Hour,
        QualityTrendStep.ThreeHours,
        QualityTrendStep.SixHours,
        QualityTrendStep.TwelveHours,
        QualityTrendStep.Day,
        QualityTrendStep.TwoDays,
    ];

    public static TimeSpan Length(QualityTrendStep step) => step switch
    {
        QualityTrendStep.Hour => TimeSpan.FromHours(1),
        QualityTrendStep.ThreeHours => TimeSpan.FromHours(3),
        QualityTrendStep.SixHours => TimeSpan.FromHours(6),
        QualityTrendStep.TwelveHours => TimeSpan.FromHours(12),
        QualityTrendStep.Day => TimeSpan.FromDays(1),
        QualityTrendStep.TwoDays => TimeSpan.FromDays(2),
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "A trend steps by one of the lengths this domain names."),
    };

    public static IReadOnlyList<QualityTrendStep> NoFinerThan(QualityTrendStep finest)
    {
        if (!Enum.IsDefined(finest))
        {
            throw new ArgumentOutOfRangeException(nameof(finest), finest, "A trend steps by one of the lengths this domain names.");
        }

        return [.. All.SkipWhile(step => step != finest)];
    }
}
