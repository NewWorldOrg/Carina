using System.Collections.Concurrent;

using Carina.Domain.Auth;
using Carina.Domain.DataBroadcast;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// The Hybridcast applications started on live channels, held in memory under the digest of the ticket their frame
/// carries, for as long as that ticket can still be taken and the grant it opens lasts. A bounded number is held,
/// and the oldest goes first.
/// </summary>
public sealed class HybridcastLaunchShelf(TimeProvider clock, PlaybackGrantPolicy policy) : IHybridcastLaunches
{
    public const int MostHeld = 64;

    private readonly ConcurrentDictionary<string, Kept> held = new(StringComparer.Ordinal);

    private readonly Lock keeping = new();

    public void Keep(string carrier, HybridcastLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);

        string digest = PlaybackGrant.DigestOf(Unguessable.Validated(carrier, nameof(carrier)));

        lock (keeping)
        {
            foreach (KeyValuePair<string, Kept> lapsed in held.Where(entry => HasLapsed(entry.Value)).ToList())
            {
                held.TryRemove(lapsed);
            }

            if (held.Count >= MostHeld)
            {
                held.TryRemove(held.MinBy(entry => entry.Value.At));
            }

            held[digest] = new Kept(launch, clock.GetUtcNow());
        }
    }

    public HybridcastLaunch? Find(string? carrier)
    {
        if (!Unguessable.IsOne(carrier) || !held.TryGetValue(PlaybackGrant.DigestOf(carrier!), out Kept? kept))
        {
            return null;
        }

        return HasLapsed(kept) ? null : kept.Launch;
    }

    private bool HasLapsed(Kept kept) => clock.GetUtcNow() >= kept.At + PlaybackTicketPolicy.LongestLifetime + policy.Lifetime;

    private sealed record Kept(HybridcastLaunch Launch, DateTimeOffset At);
}
