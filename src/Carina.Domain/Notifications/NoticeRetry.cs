namespace Carina.Domain.Notifications;

public static class NoticeRetry
{
    public static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(10);

    private static readonly IReadOnlyList<TimeSpan> Waits =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        LongestWait,
    ];

    public static int MostAttempts => Waits.Count + 1;

    public static TimeSpan? After(int failedAttempts, TimeSpan? asked = null)
    {
        if (failedAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(failedAttempts), failedAttempts, "A wait follows at least one failure.");
        }

        if (failedAttempts > Waits.Count)
        {
            return null;
        }

        return asked is { } wanted ? Capped(wanted) : Waits[failedAttempts - 1];
    }

    private static TimeSpan Capped(TimeSpan asked)
    {
        if (asked < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(asked), asked, "A wait is not negative.");
        }

        return asked > LongestWait ? LongestWait : asked;
    }
}
