using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// One thing a viewer did with a skip button, kept for good under the recording's id by value: what
/// <see cref="ViewerAction"/> says, and nothing of who did it. A button offers one of <see cref="KindsOffered"/>;
/// at most <see cref="MostAnHour"/> events are taken for one recording in any hour, and the moments one names lie
/// within the recording, to <see cref="Resolution"/>.
/// </summary>
public sealed class SkipEvent
{
    public const int MostAnHour = 600;

    public static readonly TimeSpan Within = TimeSpan.FromHours(1);

    public static readonly TimeSpan Resolution = TimeSpan.FromMilliseconds(10);

    private SkipEvent()
    {
    }

    public static IReadOnlyList<SegmentKind> KindsOffered { get; } = [SegmentKind.Break, SegmentKind.Opening, SegmentKind.Ending];

    public long Id { get; private set; }

    public RecordingId RecordingId { get; private set; } = null!;

    public DateTime HappenedAt { get; private set; }

    public ViewerActionKind Event { get; private set; }

    public SegmentKind Kind { get; private set; }

    public TimeSpan From { get; private set; }

    public TimeSpan To { get; private set; }

    public int? ModelVersion { get; private set; }

    /// <summary>
    /// What the viewer did, as learning reads it.
    /// </summary>
    public ViewerAction Action() => new(HappenedAt, Event, Kind, From, To, ModelVersion);

    /// <summary>
    /// The event, not yet kept, of <paramref name="action"/> on the recording.
    /// </summary>
    public static SkipEvent Of(RecordingId recordingId, ViewerAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return Rehydrate(0, recordingId, action.HappenedAt, action.Action, action.Kind, action.From, action.To, action.ModelVersion);
    }

    public static SkipEvent Rehydrate(
        long id,
        RecordingId recordingId,
        DateTime happenedAt,
        ViewerActionKind action,
        SegmentKind kind,
        TimeSpan from,
        TimeSpan to,
        int? modelVersion)
    {
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentOutOfRangeException.ThrowIfNegative(id);

        ViewerAction checkedAction = new(happenedAt, action, kind, from, to, modelVersion);

        return new SkipEvent
        {
            Id = id,
            RecordingId = recordingId,
            HappenedAt = checkedAction.HappenedAt,
            Event = checkedAction.Action,
            Kind = checkedAction.Kind,
            From = checkedAction.From,
            To = checkedAction.To,
            ModelVersion = checkedAction.ModelVersion,
        };
    }

    /// <summary>
    /// The moment <paramref name="seconds"/> into a recording written for <paramref name="written"/>, to
    /// <see cref="Resolution"/>; none when it lies before the recording's zero or after its length.
    /// </summary>
    public static TimeSpan? Moment(double seconds, TimeSpan written)
    {
        if (!double.IsFinite(seconds))
        {
            return null;
        }

        double hundredths = Math.Round(seconds * 100, MidpointRounding.AwayFromZero);

        return hundredths >= 0 && hundredths <= Math.Round(written.TotalSeconds * 100, MidpointRounding.AwayFromZero)
            ? TimeSpan.FromTicks((long)hundredths * Resolution.Ticks)
            : null;
    }
}
