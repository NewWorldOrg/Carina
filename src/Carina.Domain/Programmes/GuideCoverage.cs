using Carina.Domain.Base;

namespace Carina.Domain.Programmes;

/// <summary>
/// Whether a service's guide reaches as far as the broadcaster's schedule does: the schedule
/// starts at midnight Japan time, and the wanted coverage is counted from there, less the last
/// three-hour segment.
/// </summary>
public static class GuideCoverage
{
    public static readonly TimeSpan LastSegment = TimeSpan.FromHours(3);

    public static DateTime ScheduleStartOf(DateTime now)
    {
        DateTime midnight = JapanTimeZone.FromUtc(UtcTimes.Required(now, nameof(now))).Date;

        return TimeZoneInfo.ConvertTimeToUtc(midnight, JapanTimeZone.Instance);
    }

    public static DateTime WantedReachAt(DateTime now, TimeSpan wanted)
        => ScheduleStartOf(now) + wanted - LastSegment;

    public static bool IsMet(DateTime? coveredUntil, DateTime now, TimeSpan wanted)
        => UtcTimes.Optional(coveredUntil, nameof(coveredUntil)) is { } reach
            && reach >= WantedReachAt(now, wanted);
}
