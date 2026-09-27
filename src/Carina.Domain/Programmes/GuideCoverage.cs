using Carina.Domain.Base;

namespace Carina.Domain.Programmes;

/// <summary>
/// Whether a service's guide reaches as far as the broadcaster's schedule did when its stream was
/// last heard: the schedule starts at midnight Japan time on that day, and the wanted coverage is
/// counted from there, less the last three-hour segment.
/// </summary>
public static class GuideCoverage
{
    public static readonly TimeSpan LastSegment = TimeSpan.FromHours(3);

    /// <summary>
    /// Returns when the stream last brought its guide in, while its visits keep to their schedule;
    /// once the next visit is <see cref="CollectionSettings.BetweenVisits"/> overdue, or nothing was
    /// ever brought in, returns <paramref name="now"/>.
    /// </summary>
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

    private static DateTime? HeardAt(StreamVisit visit)
        => visit.Outcome is VisitOutcome.Complete or VisitOutcome.BasicOnly or VisitOutcome.Incomplete
            ? visit.LastAttemptedAt
            : visit.LastCompletedAt;
}
