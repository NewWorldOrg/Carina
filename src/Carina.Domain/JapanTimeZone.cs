namespace Carina.Domain;

public static class JapanTimeZone
{
    private const string TimeZoneId = "Asia/Tokyo";

    private static readonly Lazy<TimeZoneInfo> Resolved = new(Resolve, LazyThreadSafetyMode.PublicationOnly);

    public static TimeZoneInfo Instance => Resolved.Value;

    public static DateTimeOffset Now(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), Instance);
    }

    public static DateOnly Today(TimeProvider timeProvider) => DateOnly.FromDateTime(Now(timeProvider).DateTime);

    public static DateTime FromUtc(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(utc, Instance);

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new InvalidOperationException(
                $"Time zone '{TimeZoneId}' is not available in this runtime: install the tzdata package in the image.",
                exception);
        }
    }
}
