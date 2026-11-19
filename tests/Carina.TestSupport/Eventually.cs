namespace Carina.TestSupport;

public static class Eventually
{
    public static TimeSpan Patience { get; } = TimeSpan.FromSeconds(15);

    private static TimeSpan Interval { get; } = TimeSpan.FromMilliseconds(10);

    public static async Task Happens(Func<bool> condition, string what, TimeSpan? patience = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        TimeSpan allowed = patience ?? Patience;
        long start = Environment.TickCount64;

        while (true)
        {
            bool spent = Spent(start, allowed);

            if (condition())
            {
                return;
            }

            if (spent)
            {
                throw new TimeoutException($"Did not happen within {allowed.TotalSeconds}s: {what}.");
            }

            await Task.Delay(Interval);
        }
    }

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

        while (true)
        {
            bool spent = Spent(start, allowed);
            T seen = await attempt();

            if (condition(seen))
            {
                return seen;
            }

            if (spent)
            {
                throw new TimeoutException(
                    $"Did not happen within {allowed.TotalSeconds}s: {what}. Last seen: {describe(seen)}.");
            }

            await Task.Delay(Interval);
        }
    }

    private static bool Spent(long start, TimeSpan allowed) => Environment.TickCount64 - start >= allowed.TotalMilliseconds;
}
