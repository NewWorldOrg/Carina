using Carina.Domain.Base;

namespace Carina.Domain.Segments;

/// <summary>
/// The one row of the segment settings changed from a screen: whether CM, OP and ED learning is on, and the
/// threshold a skip button is offered from, each with when it last changed, or none while it never has. No row
/// is held until somebody changes one of them.
/// </summary>
public sealed class SegmentSettings
{
    public const int TheOnlyRow = 1;

    private SegmentSettings()
    {
    }

    public int Id { get; private set; }

    public bool Learning { get; private set; }

    public DateTime? LearningChangedAt { get; private set; }

    public double SkipThreshold { get; private set; }

    public DateTime? SkipThresholdChangedAt { get; private set; }

    public static SegmentSettings LearningSwitched(bool learning, DateTime at)
        => Rehydrate(TheOnlyRow, learning, at, SegmentSettingsStanding.DefaultSkipThreshold, null);

    public static SegmentSettings Rehydrate(
        int id,
        bool learning,
        DateTime? learningChangedAt,
        double skipThreshold,
        DateTime? skipThresholdChangedAt)
    {
        if (!(skipThreshold is >= SegmentSettingsStanding.LowestSkipThreshold and <= SegmentSettingsStanding.HighestSkipThreshold))
        {
            throw new ArgumentOutOfRangeException(nameof(skipThreshold), skipThreshold, "A skip button is offered from a threshold of 0.50 to 0.95.");
        }

        return new SegmentSettings
        {
            Id = id,
            Learning = learning,
            LearningChangedAt = UtcTimes.Optional(learningChangedAt, nameof(learningChangedAt)),
            SkipThreshold = skipThreshold,
            SkipThresholdChangedAt = UtcTimes.Optional(skipThresholdChangedAt, nameof(skipThresholdChangedAt)),
        };
    }
}

/// <summary>
/// The segment settings as they stand: the row where there is one, and learning off where there is not; and how
/// likely a break, an opening or an ending is to be before a button offers to skip it, which stands at
/// <see cref="DefaultSkipThreshold"/> while no setting holds another and is set from
/// <see cref="LowestSkipThreshold"/> to <see cref="HighestSkipThreshold"/>, to the hundredth.
/// </summary>
public sealed record SegmentSettingsStanding(bool Learning, DateTime? LearningChangedAt)
{
    public const double DefaultSkipThreshold = 0.70;

    public const double LowestSkipThreshold = 0.50;

    public const double HighestSkipThreshold = 0.95;

    private const int ThresholdDigits = 2;

    public static SegmentSettingsStanding Unset { get; } = new(false, null);

    public double SkipThreshold { get; init; } = DefaultSkipThreshold;

    public DateTime? SkipThresholdChangedAt { get; init; }

    public static SegmentSettingsStanding Over(SegmentSettings? held)
        => held is null
            ? Unset
            : new SegmentSettingsStanding(held.Learning, held.LearningChangedAt)
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
    /// The row once learning and the threshold are set as named at <paramref name="at"/>, a setting taking the
    /// time only when its value moves; none when no value moves. A threshold named is one
    /// <see cref="SkipThresholdOf"/> takes.
    /// </summary>
    public SegmentSettings? Set(bool? learning, double? skipThreshold, DateTime at)
    {
        if (skipThreshold is double named && SkipThresholdOf(named) != named)
        {
            throw new ArgumentOutOfRangeException(nameof(skipThreshold), skipThreshold, "A threshold is set to the hundredth, from 0.50 to 0.95.");
        }

        bool switches = learning is bool on && on != Learning;
        bool moves = skipThreshold is double threshold && threshold != SkipThreshold;

        if (!switches && !moves)
        {
            return null;
        }

        return SegmentSettings.Rehydrate(
            SegmentSettings.TheOnlyRow,
            learning ?? Learning,
            switches ? at : LearningChangedAt,
            skipThreshold ?? SkipThreshold,
            moves ? at : SkipThresholdChangedAt);
    }
}
