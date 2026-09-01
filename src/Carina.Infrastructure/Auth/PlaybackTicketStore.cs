using System.Collections.Concurrent;

using Carina.Domain.Auth;

namespace Carina.Infrastructure.Auth;

public sealed class PlaybackTicketStore(TimeProvider clock, PlaybackTicketPolicy policy) : IPlaybackTicketStore
{
    public const int MostHeldAtOnce = 256;

    public const int MostHeldPerSubject = 8;

    private readonly ConcurrentDictionary<string, PlaybackTicket> held = new(StringComparer.Ordinal);

    private readonly Lock issuing = new();

    public int Count => held.Count;

    public IssuedPlaybackTicket? Issue(Subject subject, PlaybackTarget target)
    {
        ArgumentNullException.ThrowIfNull(subject);

        lock (issuing)
        {
            Sweep();

            if (held.Count >= MostHeldAtOnce || HeldFor(subject) >= MostHeldPerSubject)
            {
                return null;
            }

            PlaybackTicket issued = PlaybackTicket.Issue(subject, target, Now(), out string inTheClear);

            held[issued.Digest] = issued;

            return new IssuedPlaybackTicket(inTheClear, issued.LapsesAt(policy));
        }
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

    private int HeldFor(Subject subject) => held.Count(entry => entry.Value.Subject.Equals(subject));

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
