using Carina.Domain.Channels;

namespace Carina.Domain.Quality;

/// <summary>
/// Where one tuner stands on the tuner health table, and the trouble the driver says it is in, if any.
/// </summary>
public sealed record TunerStanding(QualityStanding Standing, TunerTroubleKind? Trouble = null)
{
    public bool CannotLock => Trouble is TunerTroubleKind.NoLock;
}

public static class TunerStandings
{
    /// <summary>
    /// Reads one tuner's standing from the recordings made on it, the signal it gave and the trouble an incident
    /// says it is in, weighing the recordings and the signal alike.
    /// </summary>
    public static TunerStanding Of(
        IReadOnlyList<QualityTally> recorded,
        IReadOnlyList<QualityReading> signal,
        TunerTroubleKind? trouble)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(signal);

        if (trouble is { } outOfService && TunerTroubles.TakesItOutOfService(outOfService))
        {
            return new TunerStanding(QualityStanding.MayNotBeWatchable, outOfService);
        }

        QualityStanding? worst = trouble is null ? null : QualityStanding.Warning;

        foreach (QualityStanding said in recorded.Select(Said).Concat(signal.Select(Said)).OfType<QualityStanding>())
        {
            if (worst is not { } held || Weight(said) > Weight(held))
            {
                worst = said;
            }
        }

        return new TunerStanding(worst ?? QualityStanding.Unmeasured, trouble);
    }

    /// <summary>
    /// Ranks a standing for a worst-first list: a tuner that cannot lock, then one taken out of service for another
    /// trouble, then the bands beyond a level with a tuner that is not quite well ahead of the rest of its band, then
    /// the tuners that could not be reached, then the healthy ones, and last the ones nobody measured.
    /// </summary>
    public static int Severity(TunerStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        int troubled = standing.Trouble switch
        {
            null => 0,
            TunerTroubleKind.NoLock => 14,
            { } kind when TunerTroubles.TakesItOutOfService(kind) => 13,
            _ => 1,
        };

        if (troubled > 1)
        {
            return troubled;
        }

        return (standing.Standing switch
        {
            QualityStanding.MayNotBeWatchable => 5,
            QualityStanding.Warning => 4,
            QualityStanding.Unreachable => 3,
            QualityStanding.Good => 2,
            _ => 1,
        } * 2) + troubled;
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
        QualityStanding.Unmeasured => 0,
        QualityStanding.Good => 1,
        QualityStanding.Unreachable => 2,
        QualityStanding.Warning => 3,
        QualityStanding.MayNotBeWatchable => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(standing), standing, "A tuner is read only from what was said about it."),
    };
}
