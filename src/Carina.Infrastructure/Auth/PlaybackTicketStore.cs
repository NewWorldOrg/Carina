using System.Collections.Concurrent;

using Carina.Domain.Auth;

namespace Carina.Infrastructure.Auth;

public sealed class PlaybackTicketStore(TimeProvider clock, PlaybackTicketPolicy policy) : IPlaybackTicketStore
{
    public const int MostHeldAtOnce = 256;

    private readonly ConcurrentDictionary<string, PlaybackTicket> held = new(StringComparer.Ordinal);

    private readonly Lock issuing = new();

    public int Count => held.Count;

    public IssuedPlaybackTicket? Issue(Subject subject, PlaybackTarget target)
    {
        ArgumentNullException.ThrowIfNull(subject);

        lock (issuing)
        {
            Sweep();

            if (held.Count >= MostHeldAtOnce
                || !PlaybackTicketPolicy.HasRoomFor(subject, held.Select(entry => entry.Value)))
            {
                return null;
            }

            PlaybackTicket issued = PlaybackTicket.Issue(subject, target, Now(), out string inTheClear);

            held[issued.Digest] = issued;

            return new IssuedPlaybackTicket(inTheClear, issued.LapsesAt(policy));
        }
    }

    public PlaybackTicket? Take(string? offered, PlaybackTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (offered is null
            || !Unguessable.IsOne(offered)
            || !held.TryRemove(PlaybackTicket.DigestOf(offered), out PlaybackTicket? spent))
        {
            return null;
        }

        return spent.Spend(target, Now(), policy) ? spent : null;
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
    }
}
