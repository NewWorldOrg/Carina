using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// One seek a viewer made, kept for good under the recording's id by value: what <see cref="ViewerSeek"/> says, and
/// nothing of who made it. At most <see cref="MostAnHour"/> seeks are taken for one recording in any hour, as many as
/// skip events, and the moments one names lie within the recording, to <see cref="SkipEvent.Resolution"/>.
/// </summary>
public sealed class SeekEvent
{
    public const int MostAnHour = SkipEvent.MostAnHour;

    public static readonly TimeSpan Within = SkipEvent.Within;

    private SeekEvent()
    {
    }

    public long Id { get; private set; }

    public RecordingId RecordingId { get; private set; } = null!;

    public DateTime HappenedAt { get; private set; }

    public SeekAction Event { get; private set; }

    public SeekMeans Means { get; private set; }

    public TimeSpan From { get; private set; }

    public TimeSpan To { get; private set; }

    /// <summary>
    /// The seek, as learning reads it.
    /// </summary>
    public ViewerSeek Seek() => new(HappenedAt, Event, Means, From, To);

    /// <summary>
    /// The event, not yet kept, of <paramref name="seek"/> on the recording.
    /// </summary>
    public static SeekEvent Of(RecordingId recordingId, ViewerSeek seek)
    {
        ArgumentNullException.ThrowIfNull(seek);

        return Rehydrate(0, recordingId, seek.HappenedAt, seek.Action, seek.Means, seek.From, seek.To);
    }

    public static SeekEvent Rehydrate(
        long id,
        RecordingId recordingId,
        DateTime happenedAt,
        SeekAction action,
        SeekMeans means,
        TimeSpan from,
        TimeSpan to)
    {
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentOutOfRangeException.ThrowIfNegative(id);

        ViewerSeek checkedSeek = new(happenedAt, action, means, from, to);

        return new SeekEvent
        {
            Id = id,
            RecordingId = recordingId,
            HappenedAt = checkedSeek.HappenedAt,
            Event = checkedSeek.Action,
            Means = checkedSeek.Means,
            From = checkedSeek.From,
            To = checkedSeek.To,
        };
    }
}
