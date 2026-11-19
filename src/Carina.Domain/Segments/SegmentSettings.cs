using Carina.Domain.Base;

namespace Carina.Domain.Segments;

public sealed class SegmentSettings
{
    public const int TheOnlyRow = 1;

    private SegmentSettings()
    {
    }

    public int Id { get; private set; }

    public double SkipThreshold { get; private set; }

    public DateTime? SkipThresholdChangedAt { get; private set; }

    public static SegmentSettings Rehydrate(int id, double skipThreshold, DateTime? skipThresholdChangedAt)
    {
        if (!(skipThreshold is >= SegmentSettingsStanding.LowestSkipThreshold and <= SegmentSettingsStanding.HighestSkipThreshold))
        {
            throw new ArgumentOutOfRangeException(nameof(skipThreshold), skipThreshold, "A skip button is offered from a threshold of 0.50 to 0.99.");
        }

        return new SegmentSettings
        {
            Id = id,
            SkipThreshold = skipThreshold,
            SkipThresholdChangedAt = UtcTimes.Optional(skipThresholdChangedAt, nameof(skipThresholdChangedAt)),
        };
    }
}

public sealed record SegmentSettingsStanding
{
    public const double DefaultSkipThreshold = 0.70;

    public const double LowestSkipThreshold = 0.50;

    public const double HighestSkipThreshold = 0.99;

    private const int ThresholdDigits = 2;

    public static SegmentSettingsStanding Unset { get; } = new();

    public double SkipThreshold { get; init; } = DefaultSkipThreshold;

    public DateTime? SkipThresholdChangedAt { get; init; }

    public static SegmentSettingsStanding Over(SegmentSettings? held)
        => held is null
            ? Unset
            : new SegmentSettingsStanding
            {
                SkipThreshold = held.SkipThreshold,
                SkipThresholdChangedAt = held.SkipThresholdChangedAt,
            };

    public static double? SkipThresholdOf(double asked)
        => asked is >= LowestSkipThreshold and <= HighestSkipThreshold
            ? Math.Round(asked, ThresholdDigits, MidpointRounding.AwayFromZero)
            : null;

    public SegmentSettings? Set(double skipThreshold, DateTime at)
    {
        if (SkipThresholdOf(skipThreshold) != skipThreshold)
        {
            throw new ArgumentOutOfRangeException(nameof(skipThreshold), skipThreshold, "A threshold is set to the hundredth, from 0.50 to 0.99.");
        }

        return skipThreshold == SkipThreshold
            ? null
            : SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, skipThreshold, at);
    }
}
