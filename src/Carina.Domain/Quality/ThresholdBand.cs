namespace Carina.Domain.Quality;

public enum ThresholdSense
{
    Ceiling = 1,

    Floor = 2,
}

public sealed record ThresholdBand
{
    private ThresholdBand(
        ThresholdSense sense,
        QualityThresholdKey warningKey,
        Threshold warning,
        QualityThresholdKey? unwatchableKey,
        Threshold? unwatchable)
    {
        Sense = sense;
        WarningKey = warningKey;
        Warning = warning;
        UnwatchableKey = unwatchableKey;
        Unwatchable = unwatchable;
    }

    public ThresholdSense Sense { get; }

    public QualityThresholdKey WarningKey { get; }

    public Threshold Warning { get; }

    public QualityThresholdKey? UnwatchableKey { get; }

    public Threshold? Unwatchable { get; }

    public bool Provisional => Warning.Provisional || Unwatchable?.Provisional is true;

    public static ThresholdBand Of(ThresholdSense sense, QualityThresholdKey warning, Threshold warningAt)
    {
        Named(sense);
        Named(warning);
        ArgumentNullException.ThrowIfNull(warningAt);

        return new ThresholdBand(sense, warning, warningAt, null, null);
    }

    public static ThresholdBand Of(
        ThresholdSense sense,
        QualityThresholdKey warning,
        Threshold warningAt,
        QualityThresholdKey unwatchable,
        Threshold unwatchableAt)
    {
        Named(sense);
        Named(warning);
        Named(unwatchable);
        ArgumentNullException.ThrowIfNull(warningAt);
        ArgumentNullException.ThrowIfNull(unwatchableAt);

        if (warning == unwatchable)
        {
            throw new ArgumentException(
                "The warning level and the unwatchable level are settled apart from each other, so they are kept apart from each other.",
                nameof(unwatchable));
        }

        bool ordered = sense is ThresholdSense.Ceiling
            ? unwatchableAt.Current >= warningAt.Current
            : unwatchableAt.Current <= warningAt.Current;

        if (!ordered)
        {
            throw new ArgumentException(
                "A reading passes the warning level before it passes the unwatchable one, and a band ordered the other way never warns.",
                nameof(unwatchableAt));
        }

        return new ThresholdBand(sense, warning, warningAt, unwatchable, unwatchableAt);
    }

    internal static void Named(ThresholdSense sense)
    {
        if (!Enum.IsDefined(sense))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sense),
                sense,
                "A reading is expected either to stay under a level or to stay over one.");
        }
    }

    private static void Named(QualityThresholdKey key)
    {
        if (!Enum.IsDefined(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "A level is kept under one of the keys this domain names.");
        }
    }
}
