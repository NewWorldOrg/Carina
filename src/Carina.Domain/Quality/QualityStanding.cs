namespace Carina.Domain.Quality;

public enum QualityStanding
{
    Good = 1,

    Warning = 2,

    MayNotBeWatchable = 3,

    Unmeasured = 4,

    Unsupported = 5,

    Unreachable = 6,
}

public static class QualityStandings
{
    public static readonly IReadOnlyList<QualityStanding> All =
    [
        QualityStanding.Good,
        QualityStanding.Warning,
        QualityStanding.MayNotBeWatchable,
        QualityStanding.Unmeasured,
        QualityStanding.Unsupported,
        QualityStanding.Unreachable,
    ];

    public static bool WasMeasured(QualityStanding standing)
        => standing is QualityStanding.Good or QualityStanding.Warning or QualityStanding.MayNotBeWatchable;

    public static bool WentBeyond(QualityStanding standing)
        => standing is QualityStanding.Warning or QualityStanding.MayNotBeWatchable;
}
