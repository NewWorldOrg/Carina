using Carina.Domain.Base;

namespace Carina.Domain.Notifications;

/// <summary>
/// One happening worth telling somebody of, taken once from the ledger that holds it and worded once.
/// </summary>
public sealed class Notice
{
    public const int TitleLongest = 256;

    public const int BodyLongest = 2000;

    public const int LinkLongest = 2048;

    private Notice()
    {
    }

    public NoticeId Id { get; private set; } = null!;

    public NoticeKind Kind { get; private set; }

    public Guid SourceId { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public NoticeTone Tone { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public string? Link { get; private set; }

    public DateTime TakenAt { get; private set; }

    public static Notice Take(NoticeId id, NoticeKind kind, Guid sourceId, DateTime occurredAt, NoticeWords words, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(words);

        return Rehydrate(id, kind, sourceId, occurredAt, NoticeTones.Of(kind), words.Title, words.Body, words.Link, at);
    }

    public static Notice Rehydrate(
        NoticeId id,
        NoticeKind kind,
        Guid sourceId,
        DateTime occurredAt,
        NoticeTone tone,
        string title,
        string body,
        string? link,
        DateTime takenAt)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("A notice names the row it was taken from.", nameof(sourceId));
        }

        return new Notice
        {
            Id = id,
            Kind = NoticeKinds.Named(kind),
            SourceId = sourceId,
            OccurredAt = UtcTimes.Required(occurredAt, nameof(occurredAt)),
            Tone = NoticeTones.Named(tone),
            Title = Worded(title, TitleLongest, nameof(title)),
            Body = Worded(body, BodyLongest, nameof(body)),
            Link = LinkOrNone(link),
            TakenAt = UtcTimes.Required(takenAt, nameof(takenAt)),
        };
    }

    private static string Worded(string said, int longest, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(said, parameterName);

        if (said.Length > longest)
        {
            throw new ArgumentException(
                $"This part of a notice is at most {longest} characters, but it has {said.Length}.",
                parameterName);
        }

        return said;
    }

    private static string? LinkOrNone(string? link)
    {
        if (link is null)
        {
            return null;
        }

        bool leads = Uri.TryCreate(link, UriKind.Absolute, out Uri? address)
            && (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp);

        if (!leads || link.Length > LinkLongest)
        {
            throw new ArgumentException(
                $"A link is an absolute http or https address of at most {LinkLongest} characters.",
                nameof(link));
        }

        return link;
    }
}
