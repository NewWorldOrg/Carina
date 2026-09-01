using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using Carina.Domain.Base;

namespace Carina.Domain.Auth;

public sealed record IssuedPlaybackTicket(string InTheClear, PlaybackTicket Held)
{
    public override string ToString() => "an issued playback ticket";
}

public sealed class PlaybackTicket
{
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

    public static IssuedPlaybackTicket Issue(Subject subject, PlaybackTarget target, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(target);

        string inTheClear = Unguessable.Issue();

        return new IssuedPlaybackTicket(
            inTheClear,
            new PlaybackTicket(DigestOf(inTheClear), subject, target, UtcTimes.Required(at, nameof(at))));
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

        return now >= IssuedAt + policy.Lifetime;
    }

    public bool Opens(PlaybackTarget target) => Target.Equals(target);
}
