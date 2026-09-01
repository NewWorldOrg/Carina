using System.Collections.Concurrent;

using Carina.Domain.Auth;

namespace Carina.Infrastructure.Auth;

public sealed class PlaybackTicketStore(TimeProvider clock, PlaybackTicketPolicy policy) : IPlaybackTicketStore
{
    public const int MostHeldAtOnce = 256;

    private readonly ConcurrentDictionary<string, PlaybackTicket> held = new(StringComparer.Ordinal);

    public int Count => held.Count;

    public IssuedPlaybackTicket Issue(Subject subject, PlaybackTarget target)
    {
        Sweep();

        IssuedPlaybackTicket issued = PlaybackTicket.Issue(subject, target, Now());

        held[issued.Held.Digest] = issued.Held;

        return issued;
    }

    public Subject? Spend(string? offered, PlaybackTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (offered is null
            || !Unguessable.IsOne(offered)
            || !held.TryRemove(PlaybackTicket.DigestOf(offered), out PlaybackTicket? spent))
        {
            return null;
        }

        return spent.HasLapsed(Now(), policy) || !spent.Opens(target) ? null : spent.Subject;
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private void Sweep()
    {
        DateTime now = Now();

        foreach (KeyValuePair<string, PlaybackTicket> entry in held)
        {
            if (entry.Value.HasLapsed(now, policy))
            {
                held.TryRemove(entry);
            }
        }

        while (held.Count >= MostHeldAtOnce)
        {
            KeyValuePair<string, PlaybackTicket> oldest = held
                .OrderBy(entry => entry.Value.IssuedAt)
                .First();

            held.TryRemove(oldest);
        }
    }
}
