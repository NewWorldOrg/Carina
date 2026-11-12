namespace Carina.Domain.Programmes;

public enum TunerReport
{
    Nothing = 1,

    Failed = 2,

    Reached = 3,
}

/// <summary>
/// When a stream is next worth visiting: the ordinary wait after a settled visit or after one that heard
/// part of the schedule once every service reached the wanted coverage, the retry time after one that
/// heard part of it while some service was still short, and a doubling wait while visits hear none of it.
/// </summary>
public static class CollectionBackOff
{
    public static DateTime? NotBefore(StreamVisit visit, CollectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(settings);

        if (visit.Outcome is VisitOutcome.Interrupted)
        {
            return null;
        }

        if (visit.ConsecutiveIncomplete == 0)
        {
            return visit.LastAttemptedAt + settings.BetweenVisits;
        }

        if (visit.ConsecutiveUnheard == 0)
        {
            return visit.LastAttemptedAt + (visit.ReachedTheGoal ? settings.BetweenVisits : settings.BeforeRetrying);
        }

        TimeSpan doubled = settings.BeforeRetrying * Math.Pow(2, Math.Min(visit.ConsecutiveUnheard - 1, 16));

        return visit.LastAttemptedAt
            + (doubled > settings.LongestBackOff ? settings.LongestBackOff : doubled);
    }

    /// <summary>
    /// Whether a stream may be visited at <paramref name="now"/>: it was never visited, its last visit
    /// was interrupted, or its wait is over.
    /// </summary>
    public static bool IsDue(StreamVisit? visit, DateTime now, CollectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return visit is null || NotBefore(visit, settings) is not { } waited || waited <= now;
    }

    public static bool IsWorthReportingToTheTuner(VisitOutcome outcome)
        => outcome is VisitOutcome.NoLock or VisitOutcome.NoBytes;

    /// <summary>
    /// What a visit tells the candidate it tuned with: one that could not tune is a failure, an interruption says
    /// nothing of reception, and any other reached it.
    /// </summary>
    public static TunerReport ReportToTheTuner(VisitOutcome outcome)
    {
        if (IsWorthReportingToTheTuner(outcome))
        {
            return TunerReport.Failed;
        }

        return outcome is VisitOutcome.Interrupted ? TunerReport.Nothing : TunerReport.Reached;
    }
}
