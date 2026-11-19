using Carina.Domain.Notifications;

namespace Carina.Infrastructure.Notifications;

public sealed class PushMessages(PushServerKey key, NoticeLinkBase links, TimeProvider clock)
{
    public string? PublishedKey()
    {
        try
        {
            return key.PublicKey();
        }
        catch (Exception unusable) when (unusable is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool CanSign() => PublishedKey() is not null;

    public PushMessage Telling(PushSubscription subscription, Notice notice, PushHandling handling)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(handling);

        return new PushMessage(PushPayload.For(notice), SignedFor(subscription), handling);
    }

    public PushMessage Trying(PushSubscription subscription, string title, string body, string path)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return new PushMessage(PushPayload.For(title, body, path, id: null), SignedFor(subscription), PushHandling.Usual);
    }

    private string SignedFor(PushSubscription subscription)
        => key.AuthorizationFor(subscription.Audience, links.Origin, clock.GetUtcNow());
}
