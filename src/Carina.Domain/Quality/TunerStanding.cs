namespace Carina.Domain.Quality;

/// <summary>
/// Where one tuner stands on the tuner health table, and whether it stands there because it cannot lock.
/// </summary>
public sealed record TunerStanding(QualityStanding Standing, bool CannotLock);

public static class TunerStandings
{
    /// <summary>
    /// Reads one tuner's standing from the recordings made on it, the signal it gave and whether an incident
    /// says it cannot lock, weighing the recordings and the signal alike.
    /// </summary>
    public static TunerStanding Of(
        IReadOnlyList<QualityTally> recorded,
        IReadOnlyList<QualityReading> signal,
        bool cannotLock)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(signal);

        if (cannotLock)
        {
            return new TunerStanding(QualityStanding.MayNotBeWatchable, CannotLock: true);
        }

        QualityStanding? worst = null;

        foreach (QualityStanding said in recorded.Select(Said).Concat(signal.Select(Said)).OfType<QualityStanding>())
        {
            if (worst is not { } held || Weight(said) > Weight(held))
            {
                worst = said;
            }
        }

        return new TunerStanding(worst ?? QualityStanding.Unmeasured, CannotLock: false);
    }

    /// <summary>
    /// Ranks a standing for a worst-first list: a tuner that cannot lock, then the bands beyond a level, then the
    /// tuners that could not be reached, then the healthy ones, and last the ones nobody measured.
    /// </summary>
    public static int Severity(TunerStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        if (standing.CannotLock)
        {
            return 5;
        }

        return standing.Standing switch
        {
            QualityStanding.MayNotBeWatchable => 4,
            QualityStanding.Warning => 3,
            QualityStanding.Unreachable => 2,
            QualityStanding.Good => 1,
            _ => 0,
        };
    }

    private static QualityStanding? Said(QualityTally tally) => tally.State switch
    {
        QualityState.AtOrAboveWarning when tally.MayNotBeWatchable > 0 => QualityStanding.MayNotBeWatchable,
        _ => Said(tally.State),
    };

    private static QualityStanding? Said(QualityReading reading) => Said(QualityStates.Of(reading));

    private static QualityStanding? Said(QualityState state) => state switch
    {
        QualityState.Good => QualityStanding.Good,
        QualityState.AtOrAboveWarning => QualityStanding.Warning,
        QualityState.Unmeasured => QualityStanding.Unmeasured,
        QualityState.Unreachable => QualityStanding.Unreachable,
        QualityState.NothingToMeasure or QualityState.Unsupported => null,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "A reading stands in one of the states this domain names."),
    };

    private static int Weight(QualityStanding standing) => standing switch
    {
        QualityStanding.Good => 0,
        QualityStanding.Unmeasured => 1,
        QualityStanding.Unreachable => 2,
        QualityStanding.Warning => 3,
        QualityStanding.MayNotBeWatchable => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(standing), standing, "A tuner is read only from what was said about it."),
    };
}
