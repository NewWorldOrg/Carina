using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

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

    public ViewerAction Action() => new(HappenedAt, Event, Kind, From, To, ModelVersion);

    public bool CountsWith(DateTime other) => other > HappenedAt - Within;

    public bool IsTakenBeside(IEnumerable<DateTime> kept)
    {
        ArgumentNullException.ThrowIfNull(kept);

        return kept.Count(CountsWith) < MostAnHour;
    }

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
