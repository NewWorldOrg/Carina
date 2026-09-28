namespace Carina.Domain.Programmes;

/// <summary>
/// When a stream is next worth visiting: the ordinary wait after a settled visit, the retry time after
/// one that heard part of the schedule without finishing it, and a doubling wait while visits hear none
/// of it.
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
            return visit.LastAttemptedAt + settings.BeforeRetrying;
        }

        TimeSpan doubled = settings.BeforeRetrying * Math.Pow(2, Math.Min(visit.ConsecutiveUnheard - 1, 16));

        return visit.LastAttemptedAt
            + (doubled > settings.LongestBackOff ? settings.LongestBackOff : doubled);
    }

    public static bool IsWorthReportingToTheTuner(VisitOutcome outcome)
        => outcome is VisitOutcome.NoLock or VisitOutcome.NoBytes;
}
