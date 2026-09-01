using System.Collections.Concurrent;

using Carina.Domain.Auth;

namespace Carina.Infrastructure.Auth;

public sealed class PlaybackGrantStore(TimeProvider clock, PlaybackGrantPolicy policy) : IPlaybackGrantStore
{
    public const int MostHeldAtOnce = 256;

    public const int MostHeldPerSubject = 8;

    private readonly ConcurrentDictionary<string, PlaybackGrant> held = new(StringComparer.Ordinal);

    private readonly Lock opening = new();

    public int Count => held.Count;

    public void Open(string carrier, Subject subject, PlaybackTarget target)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(target);

        lock (opening)
        {
            Sweep();

            if (held.Count >= MostHeldAtOnce)
            {
                return;
            }

            if (HeldFor(subject) >= MostHeldPerSubject)
            {
                ForgetTheOldestOf(subject);
            }

            PlaybackGrant opened = PlaybackGrant.OpenedBy(carrier, subject, target, Now());

            held[opened.Digest] = opened;
        }
    }

    public Subject? Admit(string? offered, PlaybackTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (offered is null
            || !Unguessable.IsOne(offered)
            || !held.TryGetValue(PlaybackGrant.DigestOf(offered), out PlaybackGrant? open))
        {
            return null;
        }

        if (!open.Opens(target))
        {
            return null;
        }

        if (!open.HasLapsed(Now(), policy))
        {
            return open.Subject;
        }

        held.TryRemove(new KeyValuePair<string, PlaybackGrant>(open.Digest, open));

        return null;
    }

    public int RevokeEverythingOf(Subject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        int revoked = 0;

        foreach (KeyValuePair<string, PlaybackGrant> entry in held)
        {
            if (entry.Value.BelongsTo(subject) && held.TryRemove(entry))
            {
                revoked++;
            }
        }

        return revoked;
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private int HeldFor(Subject subject) => held.Count(entry => entry.Value.BelongsTo(subject));

    private void ForgetTheOldestOf(Subject subject)
    {
        KeyValuePair<string, PlaybackGrant> oldest = held
            .Where(entry => entry.Value.BelongsTo(subject))
            .OrderBy(entry => entry.Value.OpenedAt)
            .First();

        held.TryRemove(oldest);
    }

    private void Sweep()
    {
        DateTime now = Now();

        foreach (KeyValuePair<string, PlaybackGrant> entry in held)
        {
            if (entry.Value.HasLapsed(now, policy))
            {
                held.TryRemove(entry);
            }
        }
    }
}
