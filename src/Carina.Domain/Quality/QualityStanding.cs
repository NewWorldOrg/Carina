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

    public static QualityStanding Worst(IEnumerable<QualityStanding> standings)
    {
        ArgumentNullException.ThrowIfNull(standings);

        QualityStanding worst = QualityStanding.Good;
        bool any = false;

        foreach (QualityStanding standing in standings)
        {
            int weight = Weight(standing);

            if (!any || weight > Weight(worst))
            {
                worst = standing;
            }

            any = true;
        }

        return any ? worst : QualityStanding.Unmeasured;
    }

    private static int Weight(QualityStanding standing) => standing switch
    {
        QualityStanding.Good => 0,
        QualityStanding.Unmeasured => 1,
        QualityStanding.Unreachable => 2,
        QualityStanding.Unsupported => 3,
        QualityStanding.Warning => 4,
        QualityStanding.MayNotBeWatchable => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(standing), standing, "A reading stands where this domain names."),
    };
}
