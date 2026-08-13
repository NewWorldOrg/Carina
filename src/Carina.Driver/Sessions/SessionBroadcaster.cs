using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace Carina.Driver.Sessions;

public enum SubscriberKind
{
    Viewer,
    Piggyback,
    Survey,
}

public sealed class SessionSubscription
{
    private long droppedChunks;

    internal SessionSubscription(SubscriberKind kind, Channel<byte[]> channel)
    {
        Kind = kind;
        Channel = channel;
    }

    internal Channel<byte[]> Channel { get; }

    public SubscriberKind Kind { get; }

    public ChannelReader<byte[]> Reader => Channel.Reader;

    public bool IsDisconnected { get; internal set; }

    public bool IsTruncated { get; internal set; }

    public long DroppedChunks => Interlocked.Read(ref droppedChunks);

    internal void CountDrop() => Interlocked.Increment(ref droppedChunks);
}

public sealed class SessionBroadcaster(
    int viewerCapacity = SessionBroadcaster.DefaultViewerCapacity,
    int surveyCapacity = SessionBroadcaster.DefaultSurveyCapacity,
    TimeSpan? surveyBlockLimit = null
) : IDisposable
{
    public const int DefaultViewerCapacity = 64;
    public const int DefaultSurveyCapacity = 256;

    public static readonly TimeSpan DefaultSurveyBlockLimit = TimeSpan.FromSeconds(5);

    private enum Delivery
    {
        Delivered,
        Abandoned,
        Refused,
    }

    private readonly ConcurrentDictionary<SessionSubscription, byte> subscriptions = [];
    private readonly TimeSpan blockLimit = surveyBlockLimit ?? DefaultSurveyBlockLimit;
    private readonly Lock gate = new();

    private bool closed;
    private Exception? closedBecause;

    public int SubscriberCount => subscriptions.Count;

    public SessionSubscription Subscribe(SubscriberKind kind)
    {
        SessionSubscription? subscription = null;

        var channel = kind is SubscriberKind.Survey
            ? Channel.CreateBounded<byte[]>(
                new BoundedChannelOptions(surveyCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleWriter = true,
                }
            )
            : Channel.CreateBounded<byte[]>(
                new BoundedChannelOptions(viewerCapacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleWriter = true,
                },
                _ => subscription?.CountDrop()
            );

        subscription = new SessionSubscription(kind, channel);

        lock (gate)
        {
            if (closed)
            {
                subscription.IsDisconnected = true;
                subscription.Channel.Writer.TryComplete(closedBecause);

                return subscription;
            }

            subscriptions[subscription] = 0;
        }

        return subscription;
    }

    public void Unsubscribe(SessionSubscription subscription, Exception? because = null)
    {
        if (subscriptions.TryRemove(subscription, out _))
        {
            subscription.Channel.Writer.TryComplete(because);
        }
    }

    public void Publish(ReadOnlySpan<byte> chunk, CancellationToken cancellationToken = default)
    {
        if (subscriptions.IsEmpty)
        {
            return;
        }

        var copy = chunk.ToArray();

        foreach (var entry in subscriptions)
        {
            try
            {
                Deliver(entry.Key, copy, cancellationToken);
            }
            catch (Exception error)
            {
                entry.Key.IsDisconnected = true;
                entry.Key.IsTruncated = true;
                Unsubscribe(entry.Key, error);
            }
        }
    }

    public void Dispose() => Close(null);

    public void Close(Exception? because)
    {
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            closed = true;
            closedBecause = because;
        }

        foreach (var entry in subscriptions)
        {
            Unsubscribe(entry.Key, because ?? Truncation(entry.Key));
        }
    }

    private void Deliver(
        SessionSubscription subscription,
        byte[] chunk,
        CancellationToken cancellationToken
    )
    {
        if (subscription.Kind is not SubscriberKind.Survey)
        {
            subscription.Channel.Writer.TryWrite(chunk);

            return;
        }

        switch (DeliverWithinLimit(subscription, chunk, cancellationToken))
        {
            case Delivery.Delivered:
                return;

            case Delivery.Abandoned:
                subscription.IsTruncated = true;
                subscription.CountDrop();

                return;

            default:
                subscription.IsDisconnected = true;
                subscription.IsTruncated = true;
                Unsubscribe(subscription, TooSlow());

                return;
        }
    }

    private Delivery DeliverWithinLimit(
        SessionSubscription subscription,
        byte[] chunk,
        CancellationToken cancellationToken
    )
    {
        var start = Stopwatch.GetTimestamp();

        while (true)
        {
            if (subscription.Channel.Writer.TryWrite(chunk))
            {
                return Delivery.Delivered;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Delivery.Abandoned;
            }

            var left = blockLimit - Stopwatch.GetElapsedTime(start);
            if (left <= TimeSpan.Zero)
            {
                return Delivery.Refused;
            }

            var room = subscription.Channel.Writer.WaitToWriteAsync().AsTask();

            try
            {
                if (!room.Wait((int)left.TotalMilliseconds, cancellationToken))
                {
                    return Delivery.Refused;
                }
            }
            catch (OperationCanceledException)
            {
                return Delivery.Abandoned;
            }

            if (!room.Result)
            {
                return Delivery.Delivered;
            }
        }
    }

    private Exception TooSlow() =>
        blockLimit <= TimeSpan.Zero
            ? new IOException(
                "The subscriber's buffer filled up, and this session never waits for a subscriber, so it was disconnected."
            )
            : new TimeoutException(
                $"The subscriber did not take the stream within {blockLimit}, so it was disconnected."
            );

    private static Exception? Truncation(SessionSubscription subscription) =>
        subscription.IsTruncated
            ? new IOException("The stream ended before every chunk reached this subscriber.")
            : null;
}
