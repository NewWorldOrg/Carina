using Carina.Domain.Base;

namespace Carina.Domain.Notifications;

/// <summary>
/// Where notices are sent: a webhook of one of the kinds this ledger knows, held as the sealed bytes of its address and
/// the last characters of it for telling one from another; or a browser, held as the sealed bytes of its push
/// subscription and the SHA-256 of its endpoint, which is what tells one browser from another.
/// </summary>
public sealed class NotificationDestination
{
    public const int UrlTailLength = 4;

    public const int LargestSealedUrl = 4096;

    public const int EndpointHashLength = 64;

    private NotificationDestination()
    {
    }

    public NotificationDestinationId Id { get; private set; } = null!;

    public NotificationName Name { get; private set; } = null!;

    public WebhookTarget Target { get; private set; }

    public byte[] SealedUrl { get; private set; } = [];

    public string UrlTail { get; private set; } = string.Empty;

    public string? EndpointHash { get; private set; }

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
        => Rehydrate(id, name, target, sealedUrl, urlTail, null, enabled: true, null, null, at, at);

    /// <summary>
    /// A browser to send to, held by the sealed bytes of its subscription and the hash of its endpoint.
    /// </summary>
    public static NotificationDestination Subscribe(
        NotificationDestinationId id,
        NotificationName name,
        PushSubscription subscription,
        byte[] sealedSubscription,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return Rehydrate(
            id,
            name,
            WebhookTarget.Push,
            sealedSubscription,
            subscription.Tail,
            subscription.Fingerprint,
            enabled: true,
            null,
            null,
            at,
            at);
    }

    public static NotificationDestination Rehydrate(
        NotificationDestinationId id,
        NotificationName name,
        WebhookTarget target,
        byte[] sealedUrl,
        string urlTail,
        string? endpointHash,
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
            EndpointHash = ValidatedHash(target, endpointHash),
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
        if (Target is WebhookTarget.Push || target is WebhookTarget.Push)
        {
            throw new InvalidOperationException("A browser is not pointed at an address, and a webhook does not become a browser.");
        }

        Target = Named(target);
        SealedUrl = ValidatedSeal(sealedUrl);
        UrlTail = ValidatedTail(urlTail);
        ChangedAt = UtcTimes.Required(at, nameof(at));
        LastResultAt = null;
        LastFailure = null;
    }

    /// <summary>
    /// Takes another subscription of the same browser in place of the one held, and forgets what came of the last
    /// send to the old one. The name, and whether it is turned on, stay.
    /// </summary>
    public void Resubscribe(PushSubscription subscription, byte[] sealedSubscription, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        if (Target is not WebhookTarget.Push)
        {
            throw new InvalidOperationException("Only a browser takes a subscription.");
        }

        SealedUrl = ValidatedSeal(sealedSubscription);
        UrlTail = ValidatedTail(subscription.Tail);
        EndpointHash = ValidatedHash(Target, subscription.Fingerprint);
        ChangedAt = UtcTimes.Required(at, nameof(at));
        LastResultAt = null;
        LastFailure = null;
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

    /// <summary>
    /// Keeps that the address does not open with the keys held. The destination stays on, so that writing the
    /// address again is all it takes to send to it.
    /// </summary>
    public void NoteUnreadable(DateTime at) => NoteFailed(NoticeFailure.Unreadable, at);

    /// <summary>
    /// Keeps that the other end turned one notice down while its address still stands. The destination stays on.
    /// </summary>
    public void NoteTurnedDown(DateTime at)
    {
        LastFailure = NoticeFailure.Rejected;
        LastResultAt = UtcTimes.Required(at, nameof(at));
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

    private static string? ValidatedHash(WebhookTarget target, string? endpointHash)
    {
        if (target is not WebhookTarget.Push)
        {
            return endpointHash is null
                ? null
                : throw new ArgumentException("Only a browser is held with the hash of its endpoint.", nameof(endpointHash));
        }

        if (endpointHash is null || endpointHash.Length != EndpointHashLength || !endpointHash.All(IsLowerHex))
        {
            throw new ArgumentException(
                $"A browser is held with the SHA-256 of its endpoint, {EndpointHashLength} characters of lowercase hex.",
                nameof(endpointHash));
        }

        return endpointHash;
    }

    private static bool IsLowerHex(char letter) => letter is (>= '0' and <= '9') or (>= 'a' and <= 'f');

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
