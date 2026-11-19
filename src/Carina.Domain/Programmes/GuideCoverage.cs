using Carina.Domain.Base;

namespace Carina.Domain.Programmes;

public static class GuideCoverage
{
    public static readonly TimeSpan LastSegment = TimeSpan.FromHours(3);

    public static DateTime MeasuredFrom(StreamVisit? visit, DateTime now, CollectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        UtcTimes.Required(now, nameof(now));

        if (visit is null || HeardAt(visit) is not { } heard)
        {
            return now;
        }

        DateTime due = CollectionBackOff.NotBefore(visit, settings) ?? visit.LastAttemptedAt;

        return now > due + settings.BetweenVisits ? now : heard;
    }

    public static DateTime ScheduleStartOf(DateTime at)
    {
        DateTime midnight = JapanTimeZone.FromUtc(UtcTimes.Required(at, nameof(at))).Date;

        return TimeZoneInfo.ConvertTimeToUtc(midnight, JapanTimeZone.Instance);
    }

    public static DateTime WantedReachFrom(DateTime measuredFrom, TimeSpan wanted)
        => ScheduleStartOf(measuredFrom) + wanted - LastSegment;

    public static bool IsMet(DateTime? coveredUntil, DateTime measuredFrom, TimeSpan wanted)
        => UtcTimes.Optional(coveredUntil, nameof(coveredUntil)) is { } reach
            && reach >= WantedReachFrom(measuredFrom, wanted);

    public static bool IsMetByEveryServiceHoldingAGuide(
        IReadOnlyList<DateTime?> coveredUntil,
        DateTime measuredFrom,
        TimeSpan wanted)
    {
        ArgumentNullException.ThrowIfNull(coveredUntil);

        DateTime?[] held = [.. coveredUntil.Where(until => until is not null)];

        return held.Length > 0 && held.All(until => IsMet(until, measuredFrom, wanted));
    }

    private static DateTime? HeardAt(StreamVisit visit)
        => visit.Outcome is VisitOutcome.Complete or VisitOutcome.BasicOnly or VisitOutcome.Incomplete
            ? visit.LastAttemptedAt
            : visit.LastCompletedAt;
}
