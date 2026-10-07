using Carina.Domain.Base;

namespace Carina.Domain.Segments;

/// <summary>
/// The one row of the segment settings changed from a screen: whether CM, OP and ED learning is on,
/// and when it last changed. No row is held until somebody switches learning.
/// </summary>
public sealed class SegmentSettings
{
    public const int TheOnlyRow = 1;

    private SegmentSettings()
    {
    }

    public int Id { get; private set; }

    public bool Learning { get; private set; }

    public DateTime LearningChangedAt { get; private set; }

    public static SegmentSettings LearningSwitched(bool learning, DateTime at)
        => Rehydrate(TheOnlyRow, learning, at);

    public static SegmentSettings Rehydrate(int id, bool learning, DateTime learningChangedAt)
        => new()
        {
            Id = id,
            Learning = learning,
            LearningChangedAt = UtcTimes.Required(learningChangedAt, nameof(learningChangedAt)),
        };
}

/// <summary>
/// The segment settings as they stand: the row where there is one, and learning off where there is not.
/// </summary>
public sealed record SegmentSettingsStanding(bool Learning, DateTime? LearningChangedAt)
{
    public static SegmentSettingsStanding Unset { get; } = new(false, null);

    public static SegmentSettingsStanding Over(SegmentSettings? held)
        => held is null ? Unset : new SegmentSettingsStanding(held.Learning, held.LearningChangedAt);
}
