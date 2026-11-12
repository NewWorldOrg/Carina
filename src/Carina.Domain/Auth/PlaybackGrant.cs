using Carina.Domain.Base;

namespace Carina.Domain.Auth;

public sealed record PlaybackGrantPolicy
{
    public const int MostHeldPerSubject = 8;

    public static readonly TimeSpan LongestLifetime = TimeSpan.FromHours(6);

    public PlaybackGrantPolicy(TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lifetime, LongestLifetime);

        Lifetime = lifetime;
    }

    public static PlaybackGrantPolicy Default { get; } = new(TimeSpan.FromHours(2));

    public TimeSpan Lifetime { get; }

    /// <summary>
    /// The grant of <paramref name="subject"/> to forget before another is opened for it, among those
    /// <paramref name="held"/>: the one opened first, once the subject holds <see cref="MostHeldPerSubject"/>;
    /// nothing while it holds fewer.
    /// </summary>
    public static PlaybackGrant? TheOneToForgetBeforeOpeningFor(Subject subject, IEnumerable<PlaybackGrant> held)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(held);

        PlaybackGrant[] theirs = [.. held.Where(grant => grant.BelongsTo(subject))];

        return theirs.Length >= MostHeldPerSubject ? theirs.MinBy(grant => grant.OpenedAt) : null;
    }
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
