using Carina.Domain.Base;

namespace Carina.Domain.Notifications;

/// <summary>
/// Where notices are sent: a webhook of one of the kinds this ledger knows, held as the sealed bytes of its address and
/// the last characters of it for telling one from another.
/// </summary>
public sealed class NotificationDestination
{
    public const int UrlTailLength = 4;

    public const int LargestSealedUrl = 4096;

    private NotificationDestination()
    {
    }

    public NotificationDestinationId Id { get; private set; } = null!;

    public NotificationName Name { get; private set; } = null!;

    public WebhookTarget Target { get; private set; }

    public byte[] SealedUrl { get; private set; } = [];

    public string UrlTail { get; private set; } = string.Empty;

    public bool Enabled { get; private set; }

    public DateTime? LastResultAt { get; private set; }

    public NoticeFailure? LastFailure { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime ChangedAt { get; private set; }

    public static NotificationDestination Register(
        NotificationDestinationId id,
        NotificationName name,
        WebhookTarget target,
        byte[] sealedUrl,
        string urlTail,
        DateTime at)
        => Rehydrate(id, name, target, sealedUrl, urlTail, enabled: true, null, null, at, at);

    public static NotificationDestination Rehydrate(
        NotificationDestinationId id,
        NotificationName name,
        WebhookTarget target,
        byte[] sealedUrl,
        string urlTail,
        bool enabled,
        DateTime? lastResultAt,
        NoticeFailure? lastFailure,
        DateTime createdAt,
        DateTime changedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(name);

        if (lastFailure is not null && lastResultAt is null)
        {
            throw new ArgumentException("A failure is kept with when it was seen.", nameof(lastFailure));
        }

        return new NotificationDestination
        {
            Id = id,
            Name = name,
            Target = Named(target),
            SealedUrl = ValidatedSeal(sealedUrl),
            UrlTail = ValidatedTail(urlTail),
            Enabled = enabled,
            LastResultAt = UtcTimes.Optional(lastResultAt, nameof(lastResultAt)),
            LastFailure = lastFailure is { } failure ? NoticeFailures.Named(failure) : null,
            CreatedAt = UtcTimes.Required(createdAt, nameof(createdAt)),
            ChangedAt = UtcTimes.Required(changedAt, nameof(changedAt)),
        };
    }

    public void Rename(NotificationName name, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
        ChangedAt = UtcTimes.Required(at, nameof(at));
    }

    public void PointAt(WebhookTarget target, byte[] sealedUrl, string urlTail, DateTime at)
    {
        Target = Named(target);
        SealedUrl = ValidatedSeal(sealedUrl);
        UrlTail = ValidatedTail(urlTail);
        ChangedAt = UtcTimes.Required(at, nameof(at));
    }

    public void Enable(DateTime at)
    {
        Enabled = true;
        ChangedAt = UtcTimes.Required(at, nameof(at));
    }

    public void Disable(DateTime at)
    {
        Enabled = false;
        ChangedAt = UtcTimes.Required(at, nameof(at));
    }

    public void NoteDelivered(DateTime at)
    {
        LastResultAt = UtcTimes.Required(at, nameof(at));
        LastFailure = null;
    }

    /// <summary>
    /// Keeps the failure of the last attempt. A destination the other end rejected is turned off until somebody turns
    /// it on again.
    /// </summary>
    public void NoteFailed(NoticeFailure failure, DateTime at)
    {
        LastFailure = NoticeFailures.Named(failure);
        LastResultAt = UtcTimes.Required(at, nameof(at));

        if (failure is NoticeFailure.Rejected)
        {
            Disable(at);
        }
    }

    private static WebhookTarget Named(WebhookTarget target)
        => Enum.IsDefined(target)
            ? target
            : throw new ArgumentOutOfRangeException(nameof(target), target, "A webhook is of one of the kinds the ledger holds.");

    private static byte[] ValidatedSeal(byte[] sealedUrl)
    {
        ArgumentNullException.ThrowIfNull(sealedUrl);

        if (sealedUrl.Length is 0 or > LargestSealedUrl)
        {
            throw new ArgumentException(
                $"A sealed address is 1 to {LargestSealedUrl} bytes, but this one has {sealedUrl.Length}.",
                nameof(sealedUrl));
        }

        return [.. sealedUrl];
    }

    private static string ValidatedTail(string urlTail)
    {
        ArgumentNullException.ThrowIfNull(urlTail);

        if (urlTail.Length != UrlTailLength || urlTail.Any(letter => char.IsControl(letter) || char.IsWhiteSpace(letter)))
        {
            throw new ArgumentException(
                $"The tail of an address is its last {UrlTailLength} characters, none of them a space.",
                nameof(urlTail));
        }

        return urlTail;
    }
}
