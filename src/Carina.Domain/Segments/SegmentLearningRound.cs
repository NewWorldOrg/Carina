using Carina.Domain.Base;

namespace Carina.Domain.Segments;

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

    public SegmentMetrics? CandidateMetrics { get; private set; }

    public EvaluationEvidence? CandidateEvidence { get; private set; }

    public static SegmentLearningRound Taken(DateTime at, int? candidate = null) => Rehydrate(DayOf(at), at, candidate);

    public static SegmentLearningRound Ended(
        DateOnly day,
        DateTime takenAt,
        SegmentTrainingOutcome outcome,
        int? candidate = null,
        AdoptionVerdict? verdict = null,
        SegmentMetrics? inUseMetrics = null,
        int? inUseVersion = null,
        SegmentMetrics? candidateMetrics = null,
        EvaluationEvidence? candidateEvidence = null)
        => Rehydrate(day, takenAt, candidate, outcome, verdict, inUseMetrics, inUseVersion, candidateMetrics, candidateEvidence);

    public static bool IsKept(SegmentTrainingOutcome outcome) => outcome is not SegmentTrainingOutcome.Stopped;

    public static SegmentLearningRound Rehydrate(
        DateOnly day,
        DateTime takenAt,
        int? candidate,
        SegmentTrainingOutcome? outcome = null,
        AdoptionVerdict? verdict = null,
        SegmentMetrics? inUseMetrics = null,
        int? inUseVersion = null,
        SegmentMetrics? candidateMetrics = null,
        EvaluationEvidence? candidateEvidence = null)
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

        if (outcome is SegmentTrainingOutcome given && !IsKept(given))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A round that stopped partway is taken again, not kept as finished.");
        }

        if (outcome is SegmentTrainingOutcome said && Learned.Contains(said) != (candidate is not null && verdict is not null))
        {
            throw new ArgumentException("A round keeps a candidate, and the verdict on it, exactly when it learned one.", nameof(outcome));
        }

        if (candidate is null && (verdict is not null || inUseMetrics is not null || inUseVersion is not null || candidateMetrics is not null || candidateEvidence is not null))
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
            CandidateMetrics = candidateMetrics,
            CandidateEvidence = candidateEvidence,
        };
    }

    public static DateOnly DayOf(DateTime at)
        => DateOnly.FromDateTime(JapanTimeZone.FromUtc(UtcTimes.Required(at, nameof(at))) - DueAt);

    public static DateTime NextNotBefore(DateTime now, bool finishedToday)
    {
        DateOnly today = DayOf(now);
        DateOnly due = finishedToday ? today.AddDays(1) : today;

        return TimeZoneInfo.ConvertTimeToUtc(due.ToDateTime(TimeOnly.FromTimeSpan(DueAt)), JapanTimeZone.Instance);
    }

    public static bool HasNews(DateTime? lastLearnedFrom, DateTime? lastChanged)
        => lastLearnedFrom is not DateTime since || lastChanged > since;
}
