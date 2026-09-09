namespace Carina.Domain.Base;

public static class BroadcastDay
{
    public static readonly TimeSpan StartsAt = TimeSpan.FromHours(4);

    public static DayOfWeek Of(DateTime utc)
        => (JapanTimeZone.FromUtc(UtcTimes.Required(utc, nameof(utc))) - StartsAt).DayOfWeek;
}
