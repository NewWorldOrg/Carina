using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Carina.Domain.Base;

namespace Carina.Domain.Auth;

public sealed record IssuedPlaybackTicket(string InTheClear, DateTime LapsesAt)
{
    public override string ToString() => "an issued playback ticket";
}

public sealed class PlaybackTicket
{
    private int spent;

    private PlaybackTicket(string digest, Subject subject, PlaybackTarget target, DateTime issuedAt)
    {
        Digest = digest;
        Subject = subject;
        Target = target;
        IssuedAt = issuedAt;
    }

    public string Digest { get; }

    public Subject Subject { get; }

    public PlaybackTarget Target { get; }

    public DateTime IssuedAt { get; }

    public static PlaybackTicket Issue(
        Subject subject,
        PlaybackTarget target,
        DateTime at,
        out string inTheClear)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(target);

        inTheClear = Unguessable.Issue();

        return new PlaybackTicket(
            DigestOf(inTheClear),
            subject,
            target,
            UtcTimes.Required(at, nameof(at)));
    }

    public static string DigestOf(string offered)
    {
        ArgumentNullException.ThrowIfNull(offered);

        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(offered)));
    }

    public bool HasLapsed(DateTime now, PlaybackTicketPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        UtcTimes.Required(now, nameof(now));

        return now >= LapsesAt(policy);
    }

    public DateTime LapsesAt(PlaybackTicketPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return IssuedAt + policy.Lifetime;
    }

    public bool Opens(PlaybackTarget target) => Target.Equals(target);

    /// <summary>
    /// Spends the ticket on <paramref name="target"/> at <paramref name="now"/>: whether it lets the caller in. A
    /// ticket is spent once, whatever the answer, so every later attempt is refused.
    /// </summary>
    public bool Spend(PlaybackTarget target, DateTime now, PlaybackTicketPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (Interlocked.Exchange(ref spent, 1) != 0)
        {
            return false;
        }

        return !HasLapsed(now, policy) && Opens(target);
    }
}
