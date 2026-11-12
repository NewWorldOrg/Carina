using System.Collections.Concurrent;

using Carina.Domain.Auth;

namespace Carina.Infrastructure.Auth;

public sealed class LoginThrottle(LoginRatePolicy policy, TimeProvider clock) : ILoginThrottle
{
    private readonly ConcurrentDictionary<string, LoginTries> tries = new(StringComparer.Ordinal);

    public DateTime? TakeTry(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        LoginTries held = tries.GetOrAdd(key, _ => new LoginTries());

        lock (held)
        {
            return held.Take(clock.GetUtcNow().UtcDateTime, policy);
        }
    }

    public void Passed(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        tries.TryRemove(key, out _);
    }
}
