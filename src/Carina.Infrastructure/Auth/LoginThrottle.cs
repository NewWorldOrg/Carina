using System.Collections.Concurrent;

using Carina.Domain.Auth;

namespace Carina.Infrastructure.Auth;

public sealed class LoginThrottle(LoginRatePolicy policy, TimeProvider clock) : ILoginThrottle
{
    private readonly ConcurrentDictionary<string, List<DateTime>> tries = new(StringComparer.Ordinal);

    public DateTime? TakeTry(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        List<DateTime> held = tries.GetOrAdd(key, _ => []);

        lock (held)
        {
            Forget(held);

            if (held.Count >= policy.FailuresBeforeRefusing)
            {
                return held[0] + policy.Window;
            }

            held.Add(clock.GetUtcNow().UtcDateTime);

            return null;
        }
    }

    public void Passed(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        tries.TryRemove(key, out _);
    }

    private void Forget(List<DateTime> held)
    {
        DateTime edge = clock.GetUtcNow().UtcDateTime - policy.Window;

        held.RemoveAll(moment => moment <= edge);
    }
}
