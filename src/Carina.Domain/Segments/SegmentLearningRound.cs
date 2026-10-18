using Carina.Domain.Base;

namespace Carina.Domain.Segments;

/// <summary>
/// The day's round of learning, taken once a day: the day it is for and when it was taken. A day runs from
/// <see cref="DueAt"/> in Japan to the same time the next day, so a round is due again only once the next day
/// has begun, whenever the last one was taken.
/// </summary>
public sealed class SegmentLearningRound
{
    public static readonly TimeSpan DueAt = TimeSpan.FromHours(3);

    private SegmentLearningRound()
    {
    }

    public DateOnly Day { get; private set; }

    public DateTime TakenAt { get; private set; }

    /// <summary>
    /// The round taken at <paramref name="at"/>, for the day <paramref name="at"/> falls in.
    /// </summary>
    public static SegmentLearningRound Taken(DateTime at) => Rehydrate(DayOf(at), at);

    public static SegmentLearningRound Rehydrate(DateOnly day, DateTime takenAt)
    {
        DateTime taken = UtcTimes.Required(takenAt, nameof(takenAt));

        if (DayOf(taken) < day)
        {
            throw new ArgumentException("A round is taken no earlier than the day it is for.", nameof(takenAt));
        }

        return new SegmentLearningRound
        {
            Day = day,
            TakenAt = taken,
        };
    }

    /// <summary>
    /// The day <paramref name="at"/> falls in: the date in Japan of the latest <see cref="DueAt"/> no later than it.
    /// </summary>
    public static DateOnly DayOf(DateTime at)
        => DateOnly.FromDateTime(JapanTimeZone.FromUtc(UtcTimes.Required(at, nameof(at))) - DueAt);
}
