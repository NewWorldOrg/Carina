using Carina.Domain.Base;

namespace Carina.Domain.Segments;

public enum ViewerActionKind
{
    Skipped = 1,

    Dismissed = 2,

    Returned = 3,
}

public sealed record ViewerAction
{
    public ViewerAction(DateTime happenedAt, ViewerActionKind action, SegmentKind kind, TimeSpan from, TimeSpan to, int? modelVersion)
    {
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "A viewer skips, closes the button or comes back.");
        }

        if (!Enum.IsDefined(kind) || kind is SegmentKind.Programme)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A button offers to skip a break, an opening or an ending.");
        }

        if (from < TimeSpan.Zero || to < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(from), from, "An action names moments no earlier than the recording's own zero.");
        }

        if (modelVersion is int version && version < SegmentModelVersion.FirstNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(modelVersion), modelVersion, "A version is numbered from the first.");
        }

        HappenedAt = UtcTimes.Required(happenedAt, nameof(happenedAt));
        Action = action;
        Kind = kind;
        From = from;
        To = to;
        ModelVersion = modelVersion;
    }

    public DateTime HappenedAt { get; }

    public ViewerActionKind Action { get; }

    public SegmentKind Kind { get; }

    public TimeSpan From { get; }

    public TimeSpan To { get; }

    public int? ModelVersion { get; }
}
