using Carina.Domain.Base;

namespace Carina.Domain.Segments;

/// <summary>
/// An answer a person gave for one recording, as learning reads it: the stretches it lays end to end from
/// the recording's zero, whether it was confirmed, when it was last sent, whether it was withdrawn, and
/// the stretches the detection gave, which an answer not confirmed is read against. With no detection
/// beside it, an answer not confirmed is read against programme throughout.
/// </summary>
public sealed record GivenAnswer
{
    public GivenAnswer(IReadOnlyList<SegmentSpan> segments, bool confirmed, DateTime answeredAt, bool withdrawn, IReadOnlyList<SegmentSpan>? detected)
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
    }

    public IReadOnlyList<SegmentSpan> Segments { get; }

    public bool Confirmed { get; }

    public DateTime AnsweredAt { get; }

    public bool Withdrawn { get; }

    public IReadOnlyList<SegmentSpan>? Detected { get; }

    /// <summary>
    /// Whether the recording is judged against this answer: it was confirmed and not withdrawn.
    /// </summary>
    public bool Judges => Confirmed && !Withdrawn;
}
