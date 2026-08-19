using System.Collections.Concurrent;

using Carina.Domain.Auth;

namespace Carina.Infrastructure.Auth;

public sealed class PendingOidcLoginStore(TimeProvider clock, OidcLoginPolicy policy) : IPendingOidcLoginStore
{
    public const int MostHeldAtOnce = 256;

    private readonly ConcurrentDictionary<string, PendingOidcLogin> held = new(StringComparer.Ordinal);

    public int Count => held.Count;

    public void Hold(PendingOidcLogin pending)
    {
        ArgumentNullException.ThrowIfNull(pending);

        Sweep();

        held[pending.State] = pending;
    }

    public PendingOidcLogin? Take(string state)
        => state is not null && held.TryRemove(state, out PendingOidcLogin? pending) ? pending : null;

    private void Sweep()
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;

        foreach (KeyValuePair<string, PendingOidcLogin> entry in held)
        {
            if (entry.Value.HasLapsed(now, policy))
            {
                held.TryRemove(entry);
            }
        }

        while (held.Count >= MostHeldAtOnce)
        {
            KeyValuePair<string, PendingOidcLogin> oldest = held
                .OrderBy(entry => entry.Value.StartedAt)
                .First();

            held.TryRemove(oldest);
        }
    }
}
