using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Carina.Driver.Sessions;

public enum SubscriberKind
{
    Viewer,
    Survey,
}

public sealed class SessionSubscription
{
    internal SessionSubscription(SubscriberKind kind, Channel<byte[]> channel)
    {
        Kind = kind;
        Channel = channel;
    }

    internal Channel<byte[]> Channel { get; }

    public SubscriberKind Kind { get; }

    public ChannelReader<byte[]> Reader => Channel.Reader;

    public bool IsDisconnected { get; internal set; }

    public long DroppedChunks { get; internal set; }
}

public sealed class SessionBroadcaster(
    int viewerCapacity = SessionBroadcaster.DefaultViewerCapacity,
    int surveyCapacity = SessionBroadcaster.DefaultSurveyCapacity
) : IDisposable
{
    public const int DefaultViewerCapacity = 64;
    public const int DefaultSurveyCapacity = 256;

    private readonly ConcurrentDictionary<SessionSubscription, byte> subscriptions = [];

    public int SubscriberCount => subscriptions.Count;

    public SessionSubscription Subscribe(SubscriberKind kind)
    {
        var options = kind switch
        {
            SubscriberKind.Viewer => new BoundedChannelOptions(viewerCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = true,
            },
            _ => new BoundedChannelOptions(surveyCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
            },
        };

        var subscription = new SessionSubscription(kind, Channel.CreateBounded<byte[]>(options));
        subscriptions[subscription] = 0;

        return subscription;
    }

    public void Unsubscribe(SessionSubscription subscription)
    {
        if (subscriptions.TryRemove(subscription, out _))
        {
            subscription.Channel.Writer.TryComplete();
        }
    }

    public void Publish(byte[] chunk)
    {
        foreach (var subscription in subscriptions.Keys)
        {
            if (subscription.Kind is SubscriberKind.Viewer)
            {
                if (subscription.Channel.Reader.Count >= viewerCapacity)
                {
                    subscription.DroppedChunks++;
                }

                subscription.Channel.Writer.TryWrite(chunk);
                continue;
            }

            if (subscription.Channel.Writer.TryWrite(chunk))
            {
                continue;
            }

            subscription.IsDisconnected = true;
            Unsubscribe(subscription);
        }
    }

    public void Dispose()
    {
        foreach (var subscription in subscriptions.Keys)
        {
            Unsubscribe(subscription);
        }
    }
}
