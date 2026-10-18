using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// The answer a person gave for one recording, kept for good under the recording's id by value: its stretches
/// end to end from the recording's zero to the length they were read against, with no probability; whether it
/// is confirmed and since when; when it was last sent and how many times; and when it was withdrawn, if it is.
/// An answer sent again takes the place of the one kept and stands again; a withdrawn one is kept as it was.
/// </summary>
public sealed class RecordingAnswer
{
    private RecordingAnswer()
    {
    }

    public RecordingId RecordingId { get; private set; } = null!;

    public IReadOnlyList<SegmentSpan> Segments { get; private set; } = [];

    public TimeSpan Length { get; private set; }

    public bool Confirmed { get; private set; }

    public DateTime? ConfirmedAt { get; private set; }

    public DateTime AnsweredAt { get; private set; }

    public int Answers { get; private set; }

    public DateTime? WithdrawnAt { get; private set; }

    /// <summary>
    /// Whether the answer stands, which is to say it has not been withdrawn since it was last sent.
    /// </summary>
    public bool Stands => WithdrawnAt is null;

    /// <summary>
    /// The share of the length the answer gives to anything but the programme.
    /// </summary>
    public double BreakShare
        => Length > TimeSpan.Zero
            ? Math.Clamp(Segments.Where(segment => segment.Kind is not SegmentKind.Programme).Sum(segment => segment.Length.Ticks) / (double)Length.Ticks, 0, 1)
            : 0;

    /// <summary>
    /// The answer sent at <paramref name="at"/>, as the first one sent for the recording: confirmed at that moment
    /// when it is confirmed.
    /// </summary>
    public static RecordingAnswer Given(RecordingId recordingId, AnsweredStretches stretches, bool confirmed, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(stretches);

        return Rehydrate(recordingId, stretches.Stretches, stretches.Length, confirmed, confirmed ? at : null, at, 1, null);
    }

    public static RecordingAnswer Rehydrate(
        RecordingId recordingId,
        IReadOnlyList<SegmentSpan> segments,
        TimeSpan length,
        bool confirmed,
        DateTime? confirmedAt,
        DateTime answeredAt,
        int answers,
        DateTime? withdrawnAt)
    {
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentOutOfRangeException.ThrowIfLessThan(answers, 1);

        if (Untiled(segments, length) is { } fault)
        {
            throw new ArgumentException(fault, nameof(segments));
        }

        if (confirmed != confirmedAt.HasValue)
        {
            throw new ArgumentException("An answer is confirmed exactly when it says since when.", nameof(confirmedAt));
        }

        return new RecordingAnswer
        {
            RecordingId = recordingId,
            Segments = [.. segments],
            Length = length,
            Confirmed = confirmed,
            ConfirmedAt = UtcTimes.Optional(confirmedAt, nameof(confirmedAt)),
            AnsweredAt = UtcTimes.Required(answeredAt, nameof(answeredAt)),
            Answers = answers,
            WithdrawnAt = UtcTimes.Optional(withdrawnAt, nameof(withdrawnAt)),
        };
    }

    private static string? Untiled(IReadOnlyList<SegmentSpan> segments, TimeSpan length)
    {
        if (segments.Count is 0 || segments.Any(segment => segment is null) || segments[0].Starts != TimeSpan.Zero || segments[^1].Ends != length)
        {
            return "An answer covers its recording from zero to the length it was read against.";
        }

        if (segments.Any(segment => segment.Probability is not null))
        {
            return "A stretch an answer gives carries no probability.";
        }

        return segments.Zip(segments.Skip(1)).Any(pair => pair.Second.Starts != pair.First.Ends)
            ? "An answer lays its stretches end to end."
            : null;
    }
}
