using Carina.Domain.Notifications;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// Writes the messages that go to a browser: the words of a notice or of a trial, signed for the push service the
/// subscription names with the server's own key and the address the screens are reached at.
/// </summary>
public sealed class PushMessages(PushServerKey key, NoticeLinkBase links, TimeProvider clock)
{
    /// <summary>
    /// Whether the server's key is there to sign with. Where it is not, nothing can be sent to any browser.
    /// </summary>
    public bool CanSign()
    {
        try
        {
            _ = key.PublicKey();

            return true;
        }
        catch (Exception unusable) when (unusable is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

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
