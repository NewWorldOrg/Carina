namespace Carina.Domain.Notifications;

/// <summary>
/// When a delivery that failed is tried again: after the first, second and third failure, and never after the fourth.
/// </summary>
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

    /// <summary>
    /// How long to wait after the given count of failed attempts, or nothing when no attempt is left. A wait the other
    /// end asked for is taken in place of the usual one, up to the longest wait.
    /// </summary>
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
