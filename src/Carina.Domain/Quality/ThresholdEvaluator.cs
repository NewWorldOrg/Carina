namespace Carina.Domain.Quality;

public static class ThresholdEvaluator
{
    public static ThresholdVerdict Judge(double? observed, ThresholdBand band)
    {
        ArgumentNullException.ThrowIfNull(band);

        if (observed is not { } reading)
        {
            return ThresholdVerdict.Of(QualityStanding.Unmeasured, null, band.WarningKey, band.Warning);
        }

        if (double.IsNaN(reading) || double.IsInfinity(reading))
        {
            throw new ArgumentOutOfRangeException(
                nameof(observed),
                reading,
                "A reading no level can be held against is not a reading.");
        }

        if (band.Unwatchable is { } unwatchable && Reaches(reading, unwatchable.Current, band.Sense))
        {
            return ThresholdVerdict.Of(QualityStanding.MayNotBeWatchable, reading, band.UnwatchableKey!.Value, unwatchable);
        }

        QualityStanding standing = Reaches(reading, band.Warning.Current, band.Sense)
            ? QualityStanding.Warning
            : QualityStanding.Good;

        return ThresholdVerdict.Of(standing, reading, band.WarningKey, band.Warning);
    }

    private static bool Reaches(double reading, double level, ThresholdSense sense)
        => sense is ThresholdSense.Ceiling ? reading >= level : reading <= level;
}
