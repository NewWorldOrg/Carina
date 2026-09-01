using Carina.Domain.Base;

namespace Carina.Domain.Auth;

public sealed record PlaybackGrantPolicy
{
    public static readonly TimeSpan LongestLifetime = TimeSpan.FromHours(6);

    public PlaybackGrantPolicy(TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lifetime, LongestLifetime);

        Lifetime = lifetime;
    }

    public static PlaybackGrantPolicy Default { get; } = new(TimeSpan.FromHours(2));

    public TimeSpan Lifetime { get; }
}

public sealed class PlaybackGrant
{
    private PlaybackGrant(string digest, Subject subject, PlaybackTarget target, DateTime openedAt)
    {
        Digest = digest;
        Subject = subject;
        Target = target;
        OpenedAt = openedAt;
    }

    public string Digest { get; }

    public Subject Subject { get; }

    public PlaybackTarget Target { get; }

    public DateTime OpenedAt { get; }

    public static PlaybackGrant OpenedBy(
        string carrier,
        Subject subject,
        PlaybackTarget target,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(target);

        return new PlaybackGrant(
            PlaybackTicket.DigestOf(Unguessable.Validated(carrier, nameof(carrier))),
            subject,
            target,
            UtcTimes.Required(at, nameof(at)));
    }

    public static string DigestOf(string offered) => PlaybackTicket.DigestOf(offered);

    public bool HasLapsed(DateTime now, PlaybackGrantPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        UtcTimes.Required(now, nameof(now));

        return now >= LapsesAt(policy);
    }

    public DateTime LapsesAt(PlaybackGrantPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return OpenedAt + policy.Lifetime;
    }

    public bool Opens(PlaybackTarget target) => Target.Equals(target);

    public bool BelongsTo(Subject subject) => Subject.Equals(subject);
}
