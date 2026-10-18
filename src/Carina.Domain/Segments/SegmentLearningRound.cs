using Carina.Domain.Base;

namespace Carina.Domain.Segments;

/// <summary>
/// The day's round of learning, kept once it finishes: the day it is for, when it was taken, and the candidate it
/// kept, if it kept one. A day runs from <see cref="DueAt"/> in Japan to the same time the next day, so a round
/// is due again only once the next day has begun, whenever the last one was taken. A round learns only when
/// something learning reads has changed since the last round that kept a candidate was taken
/// (<see cref="HasNews"/>).
/// </summary>
public sealed class SegmentLearningRound
{
    public static readonly TimeSpan DueAt = TimeSpan.FromHours(3);

    private SegmentLearningRound()
    {
    }

    public DateOnly Day { get; private set; }

    public DateTime TakenAt { get; private set; }

    public int? Candidate { get; private set; }

    /// <summary>
    /// The round taken at <paramref name="at"/>, for the day <paramref name="at"/> falls in, that kept the candidate
    /// numbered <paramref name="candidate"/>, if any.
    /// </summary>
    public static SegmentLearningRound Taken(DateTime at, int? candidate = null) => Rehydrate(DayOf(at), at, candidate);

    public static SegmentLearningRound Rehydrate(DateOnly day, DateTime takenAt, int? candidate)
    {
        DateTime taken = UtcTimes.Required(takenAt, nameof(takenAt));

        if (DayOf(taken) < day)
        {
            throw new ArgumentException("A round is taken no earlier than the day it is for.", nameof(takenAt));
        }

        if (candidate < SegmentModelVersion.FirstNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(candidate), candidate, "A version is numbered from the first.");
        }

        return new SegmentLearningRound
        {
            Day = day,
            TakenAt = taken,
            Candidate = candidate,
        };
    }

    /// <summary>
    /// The day <paramref name="at"/> falls in: the date in Japan of the latest <see cref="DueAt"/> no later than it.
    /// </summary>
    public static DateOnly DayOf(DateTime at)
        => DateOnly.FromDateTime(JapanTimeZone.FromUtc(UtcTimes.Required(at, nameof(at))) - DueAt);

    /// <summary>
    /// Whether there is anything new to learn from: always while no round has kept a candidate, and otherwise only
    /// when something learning reads changed after <paramref name="lastLearnedFrom"/>, when the last round that kept
    /// one was taken.
    /// </summary>
    public static bool HasNews(DateTime? lastLearnedFrom, DateTime? lastChanged)
        => lastLearnedFrom is not DateTime since || lastChanged > since;
}
