using System.Collections.Concurrent;

using Carina.Domain.Auth;

namespace Carina.Infrastructure.Auth;

public sealed class LoginThrottle(LoginRatePolicy policy, TimeProvider clock) : ILoginThrottle
{
    private readonly ConcurrentDictionary<string, List<DateTime>> failures = new(StringComparer.Ordinal);

    public DateTime? RefusesUntil(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!failures.TryGetValue(key, out List<DateTime>? held))
        {
            return null;
        }

        lock (held)
        {
            Forget(held);

            return held.Count >= policy.FailuresBeforeRefusing ? held[0] + policy.Window : null;
        }
    }

    public void Failed(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        List<DateTime> held = failures.GetOrAdd(key, _ => []);

        lock (held)
        {
            Forget(held);
            held.Add(clock.GetUtcNow().UtcDateTime);
        }
    }

    public void Passed(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        failures.TryRemove(key, out _);
    }

    private void Forget(List<DateTime> held)
    {
        DateTime edge = clock.GetUtcNow().UtcDateTime - policy.Window;

        held.RemoveAll(moment => moment <= edge);
    }
}
