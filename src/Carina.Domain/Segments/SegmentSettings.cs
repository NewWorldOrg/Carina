using Carina.Domain.Base;

namespace Carina.Domain.Segments;

/// <summary>
/// The one row of the segment settings changed from a screen: the threshold a skip button is offered from, with when
/// it last changed, or none while it never has. No row is held until somebody changes it.
/// </summary>
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
            throw new ArgumentOutOfRangeException(nameof(skipThreshold), skipThreshold, "A skip button is offered from a threshold of 0.50 to 0.95.");
        }

        return new SegmentSettings
        {
            Id = id,
            SkipThreshold = skipThreshold,
            SkipThresholdChangedAt = UtcTimes.Optional(skipThresholdChangedAt, nameof(skipThresholdChangedAt)),
        };
    }
}

/// <summary>
/// The segment settings as they stand: how likely a break, an opening or an ending is to be before a button offers
/// to skip it, which stands at <see cref="DefaultSkipThreshold"/> while no setting holds another and is set from
/// <see cref="LowestSkipThreshold"/> to <see cref="HighestSkipThreshold"/>, to the hundredth, and when it last
/// changed.
/// </summary>
public sealed record SegmentSettingsStanding
{
    public const double DefaultSkipThreshold = 0.70;

    public const double LowestSkipThreshold = 0.50;

    public const double HighestSkipThreshold = 0.95;

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

    /// <summary>
    /// The threshold <paramref name="asked"/> sets, to the hundredth; none when it lies outside
    /// <see cref="LowestSkipThreshold"/> to <see cref="HighestSkipThreshold"/>.
    /// </summary>
    public static double? SkipThresholdOf(double asked)
        => asked is >= LowestSkipThreshold and <= HighestSkipThreshold
            ? Math.Round(asked, ThresholdDigits, MidpointRounding.AwayFromZero)
            : null;

    /// <summary>
    /// The row once the threshold is set to <paramref name="skipThreshold"/> at <paramref name="at"/>; none when it
    /// does not move. The threshold is one <see cref="SkipThresholdOf"/> takes.
    /// </summary>
    public SegmentSettings? Set(double skipThreshold, DateTime at)
    {
        if (SkipThresholdOf(skipThreshold) != skipThreshold)
        {
            throw new ArgumentOutOfRangeException(nameof(skipThreshold), skipThreshold, "A threshold is set to the hundredth, from 0.50 to 0.95.");
        }

        return skipThreshold == SkipThreshold
            ? null
            : SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, skipThreshold, at);
    }
}
