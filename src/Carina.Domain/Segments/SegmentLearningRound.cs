using Carina.Domain.Base;

namespace Carina.Domain.Segments;

/// <summary>
/// The day's round of learning, kept once it finishes: the day it is for, when it was taken, what became of it, and,
/// when it learned a candidate, the candidate it kept, what judging it against the version in use said, the figures
/// of the version in use on the same recordings and its number. A round kept before what became of rounds was kept
/// says none of that but its candidate, and one kept before the number was kept may say the figures without it. A
/// day runs from <see cref="DueAt"/> in Japan to the same time the next day, so a round is due again only once the
/// next day has begun, whenever the last one was taken (<see cref="NextNotBefore"/>). A round learns only when
/// something learning reads has changed since the last round that kept a candidate was taken
/// (<see cref="HasNews"/>).
/// </summary>
public sealed class SegmentLearningRound
{
    public static readonly TimeSpan DueAt = TimeSpan.FromHours(3);

    private static readonly SegmentTrainingOutcome[] Learned =
    [
        SegmentTrainingOutcome.Kept,
        SegmentTrainingOutcome.Adopted,
        SegmentTrainingOutcome.HeldAfterRollback,
        SegmentTrainingOutcome.InUseChangedMeanwhile,
    ];

    private SegmentLearningRound()
    {
    }

    public DateOnly Day { get; private set; }

    public DateTime TakenAt { get; private set; }

    public int? Candidate { get; private set; }

    public SegmentTrainingOutcome? Outcome { get; private set; }

    public AdoptionVerdict? Verdict { get; private set; }

    public SegmentMetrics? InUseMetrics { get; private set; }

    public int? InUseVersion { get; private set; }

    /// <summary>
    /// The round taken at <paramref name="at"/>, for the day <paramref name="at"/> falls in, that kept the candidate
    /// numbered <paramref name="candidate"/>, if any.
    /// </summary>
    public static SegmentLearningRound Taken(DateTime at, int? candidate = null) => Rehydrate(DayOf(at), at, candidate);

    /// <summary>
    /// The round of <paramref name="day"/> taken at <paramref name="takenAt"/> that came to <paramref name="outcome"/>,
    /// with the candidate it kept, the verdict on it and the figures of the version in use, with its number, when it
    /// learned one.
    /// </summary>
    public static SegmentLearningRound Ended(
        DateOnly day,
        DateTime takenAt,
        SegmentTrainingOutcome outcome,
        int? candidate = null,
        AdoptionVerdict? verdict = null,
        SegmentMetrics? inUseMetrics = null,
        int? inUseVersion = null)
        => Rehydrate(day, takenAt, candidate, outcome, verdict, inUseMetrics, inUseVersion);

    public static SegmentLearningRound Rehydrate(
        DateOnly day,
        DateTime takenAt,
        int? candidate,
        SegmentTrainingOutcome? outcome = null,
        AdoptionVerdict? verdict = null,
        SegmentMetrics? inUseMetrics = null,
        int? inUseVersion = null)
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

        if (inUseVersion < SegmentModelVersion.FirstNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(inUseVersion), inUseVersion, "A version is numbered from the first.");
        }

        if (outcome is SegmentTrainingOutcome.Stopped)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A round that stopped partway is taken again, not kept as finished.");
        }

        if (outcome is SegmentTrainingOutcome said && Learned.Contains(said) != (candidate is not null && verdict is not null))
        {
            throw new ArgumentException("A round keeps a candidate, and the verdict on it, exactly when it learned one.", nameof(outcome));
        }

        if (candidate is null && (verdict is not null || inUseMetrics is not null || inUseVersion is not null))
        {
            throw new ArgumentException("Only a round that kept a candidate says how it was judged.", nameof(candidate));
        }

        return new SegmentLearningRound
        {
            Day = day,
            TakenAt = taken,
            Candidate = candidate,
            Outcome = outcome,
            Verdict = verdict,
            InUseMetrics = inUseMetrics,
            InUseVersion = inUseVersion,
        };
    }

    /// <summary>
    /// The day <paramref name="at"/> falls in: the date in Japan of the latest <see cref="DueAt"/> no later than it.
    /// </summary>
    public static DateOnly DayOf(DateTime at)
        => DateOnly.FromDateTime(JapanTimeZone.FromUtc(UtcTimes.Required(at, nameof(at))) - DueAt);

    /// <summary>
    /// The first moment the next round may begin, as seen at <paramref name="now"/>: the start of the day
    /// <paramref name="now"/> falls in while its round has not been finished, and of the next day once it has.
    /// When it begins after that is up to spare time and to learning being on.
    /// </summary>
    public static DateTime NextNotBefore(DateTime now, bool finishedToday)
    {
        DateOnly today = DayOf(now);
        DateOnly due = finishedToday ? today.AddDays(1) : today;

        return TimeZoneInfo.ConvertTimeToUtc(due.ToDateTime(TimeOnly.FromTimeSpan(DueAt)), JapanTimeZone.Instance);
    }

    /// <summary>
    /// Whether there is anything new to learn from: always while no round has kept a candidate, and otherwise only
    /// when something learning reads changed after <paramref name="lastLearnedFrom"/>, when the last round that kept
    /// one was taken.
    /// </summary>
    public static bool HasNews(DateTime? lastLearnedFrom, DateTime? lastChanged)
        => lastLearnedFrom is not DateTime since || lastChanged > since;
}
