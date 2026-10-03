namespace Carina.TestSupport;

public static class Eventually
{
    public static TimeSpan Patience { get; } = TimeSpan.FromSeconds(15);

    private static TimeSpan Interval { get; } = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Waits until <paramref name="condition"/> holds. It is looked at once more after the patience
    /// runs out, so a condition met during the last wait is not taken as one that never was.
    /// </summary>
    public static async Task Happens(Func<bool> condition, string what, TimeSpan? patience = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        TimeSpan allowed = patience ?? Patience;
        long start = Environment.TickCount64;

        while (!condition())
        {
            if (Environment.TickCount64 - start >= allowed.TotalMilliseconds)
            {
                throw new TimeoutException($"Did not happen within {allowed.TotalSeconds}s: {what}.");
            }

            await Task.Delay(Interval);
        }
    }

    /// <summary>
    /// Asks <paramref name="attempt"/> until what it yields meets <paramref name="condition"/>. The last
    /// answer is judged before giving up, so one that arrived during the last wait is not taken as one
    /// that never did.
    /// </summary>
    public static async Task<T> Yields<T>(
        Func<Task<T>> attempt,
        Func<T, bool> condition,
        Func<T, string> describe,
        string what,
        TimeSpan? patience = null)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(describe);

        TimeSpan allowed = patience ?? Patience;
        long start = Environment.TickCount64;
        T seen = await attempt();

        while (!condition(seen))
        {
            if (Environment.TickCount64 - start >= allowed.TotalMilliseconds)
            {
                throw new TimeoutException(
                    $"Did not happen within {allowed.TotalSeconds}s: {what}. Last seen: {describe(seen)}.");
            }

            await Task.Delay(Interval);
            seen = await attempt();
        }

        return seen;
    }
}
