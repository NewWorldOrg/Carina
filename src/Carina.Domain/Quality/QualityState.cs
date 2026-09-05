namespace Carina.Domain.Quality;

public enum QualityState
{
    Good = 1,

    AtOrAboveWarning = 2,

    Unmeasured = 3,

    NothingToMeasure = 4,

    Unsupported = 5,

    Unreachable = 6,
}

public static class QualityStates
{
    public static readonly IReadOnlyList<QualityState> All =
    [
        QualityState.Good,
        QualityState.AtOrAboveWarning,
        QualityState.Unmeasured,
        QualityState.NothingToMeasure,
        QualityState.Unsupported,
        QualityState.Unreachable,
    ];

    public static QualityState Of(QualityReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        if (!reading.Supported)
        {
            return QualityState.Unsupported;
        }

        if (!reading.Supplied)
        {
            return QualityState.Unreachable;
        }

        if (reading.Subjects is 0)
        {
            return QualityState.NothingToMeasure;
        }

        if (reading.Measured is 0)
        {
            return QualityState.Unmeasured;
        }

        return reading.BeyondThreshold > 0 ? QualityState.AtOrAboveWarning : QualityState.Good;
    }
}
