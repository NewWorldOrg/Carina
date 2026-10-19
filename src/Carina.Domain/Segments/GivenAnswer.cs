using Carina.Domain.Base;

namespace Carina.Domain.Segments;

/// <summary>
/// An answer a person gave for one recording, as learning reads it: the stretches it lays end to end from
/// the recording's zero, whether it was confirmed, when it was last sent, whether it was withdrawn, the
/// stretches the detection gave, which an answer not confirmed is read against, and whether it could name an
/// opening and an ending. With no detection beside it, an answer not confirmed is read against programme
/// throughout.
/// </summary>
public sealed record GivenAnswer
{
    public GivenAnswer(
        IReadOnlyList<SegmentSpan> segments,
        bool confirmed,
        DateTime answeredAt,
        bool withdrawn,
        IReadOnlyList<SegmentSpan>? detected,
        bool namesOpeningsAndEndings = false)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Count is 0 || segments.Any(segment => segment is null) || segments[0].Starts != TimeSpan.Zero)
        {
            throw new ArgumentException("An answer lays its stretches from the recording's zero.", nameof(segments));
        }

        if (segments.Zip(segments.Skip(1)).Any(pair => pair.Second.Starts != pair.First.Ends))
        {
            throw new ArgumentException("An answer lays its stretches end to end.", nameof(segments));
        }

        if (detected is not null && detected.Any(segment => segment is null))
        {
            throw new ArgumentException("Every stretch of the detection is handed over.", nameof(detected));
        }

        Segments = [.. segments];
        Confirmed = confirmed;
        AnsweredAt = UtcTimes.Required(answeredAt, nameof(answeredAt));
        Withdrawn = withdrawn;
        Detected = detected is null ? null : [.. detected];
        NamesOpeningsAndEndings = namesOpeningsAndEndings;
    }

    /// <summary>
    /// The answer kept for a recording, read beside the detection of the recording it is read against: one
    /// that was discarded shows nothing, and with none the answer is read against programme throughout.
    /// </summary>
    public static GivenAnswer Of(RecordingAnswer answer, RecordingDetection? detection)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (detection is not null && !detection.RecordingId.Equals(answer.RecordingId))
        {
            throw new ArgumentException("An answer is read beside the detection of its own recording.", nameof(detection));
        }

        IReadOnlyList<SegmentSpan>? shown = detection switch
        {
            null => null,
            { State: SegmentDetectionState.Discarded } => [],
            _ => detection.Segments,
        };

        return new GivenAnswer(answer.Segments, answer.Confirmed, answer.AnsweredAt, !answer.Stands, shown, answer.NamesOpeningsAndEndings);
    }

    public IReadOnlyList<SegmentSpan> Segments { get; }

    public bool Confirmed { get; }

    public DateTime AnsweredAt { get; }

    public bool Withdrawn { get; }

    public IReadOnlyList<SegmentSpan>? Detected { get; }

    public bool NamesOpeningsAndEndings { get; }

    /// <summary>
    /// Whether the recording is judged against this answer: it was confirmed and not withdrawn.
    /// </summary>
    public bool Judges => Confirmed && !Withdrawn;
}
