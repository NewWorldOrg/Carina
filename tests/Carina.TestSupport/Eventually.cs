namespace Carina.TestSupport;

public static class Eventually
{
    public static TimeSpan Patience { get; } = TimeSpan.FromSeconds(15);

    private static TimeSpan Interval { get; } = TimeSpan.FromMilliseconds(10);

    public static async Task Happens(Func<bool> condition, string what)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var start = Environment.TickCount64;

        while (Environment.TickCount64 - start < Patience.TotalMilliseconds)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(Interval);
        }

        throw new TimeoutException($"Did not happen within {Patience.TotalSeconds}s: {what}.");
    }

    public static async Task<T> Yields<T>(
        Func<Task<T>> attempt,
        Func<T, bool> condition,
        Func<T, string> describe,
        string what)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(describe);

        var start = Environment.TickCount64;
        var seen = await attempt();

        while (Environment.TickCount64 - start < Patience.TotalMilliseconds)
        {
            if (condition(seen))
            {
                return seen;
            }

            await Task.Delay(Interval);
            seen = await attempt();
        }

        throw new TimeoutException(
            $"Did not happen within {Patience.TotalSeconds}s: {what}. Last seen: {describe(seen)}.");
    }
}
