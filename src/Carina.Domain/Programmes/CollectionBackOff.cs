namespace Carina.Domain.Programmes;

public enum TunerReport
{
    Nothing = 1,

    Failed = 2,

    Reached = 3,
}

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

    public static bool IsDue(StreamVisit? visit, DateTime now, CollectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return visit is null || NotBefore(visit, settings) is not { } waited || waited <= now;
    }

    public static bool IsWorthReportingToTheTuner(VisitOutcome outcome)
        => outcome is VisitOutcome.NoLock or VisitOutcome.NoBytes;

    public static TunerReport ReportToTheTuner(VisitOutcome outcome)
    {
        if (IsWorthReportingToTheTuner(outcome))
        {
            return TunerReport.Failed;
        }

        return outcome is VisitOutcome.Interrupted ? TunerReport.Nothing : TunerReport.Reached;
    }
}
