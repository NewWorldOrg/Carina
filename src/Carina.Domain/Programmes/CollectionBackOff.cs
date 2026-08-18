namespace Carina.Domain.Programmes;

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

        var doubled = settings.BeforeRetrying * Math.Pow(2, Math.Min(visit.ConsecutiveIncomplete - 1, 16));

        return visit.LastAttemptedAt
            + (doubled > settings.LongestBackOff ? settings.LongestBackOff : doubled);
    }

    public static bool IsWorthReportingToTheTuner(VisitOutcome outcome)
        => outcome is VisitOutcome.NoLock or VisitOutcome.NoBytes;
}
