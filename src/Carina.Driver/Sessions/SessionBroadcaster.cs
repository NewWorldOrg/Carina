using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    TimeSpan? surveyBlockLimit = null,
    Action<Exception>? report = null,
    int subscriberLimit = SessionBroadcaster.DefaultSubscriberLimit
) : IDisposable
{
    public const int DefaultViewerCapacity = 64;
    public const int DefaultSurveyCapacity = 256;
    public const int DefaultSubscriberLimit = 8;

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
    private long droppedChunks;

    public int SubscriberCount => subscriptions.Count;

    public bool IsClosed
    {
        get
        {
            lock (gate)
            {
                return closed;
            }
        }
    }

    public long DroppedChunks => Interlocked.Read(ref droppedChunks);

    private void Tally() => Interlocked.Increment(ref droppedChunks);

    public int SubscriberLimit => subscriberLimit;

    public bool TrySubscribe(
        SubscriberKind kind,
        [NotNullWhen(true)] out SessionSubscription? subscription
    )
    {
        subscription = Attach(kind, subscriberLimit, acceptClosed: false);

        return subscription is not null;
    }

    public SessionSubscription Subscribe(SubscriberKind kind) =>
        Attach(kind, int.MaxValue, acceptClosed: true)!;

    private SessionSubscription? Attach(SubscriberKind kind, int limit, bool acceptClosed)
    {
        SessionSubscription? subscription = null;

        var channel = kind is SubscriberKind.Survey
            ? Channel.CreateBounded<byte[]>(
                new BoundedChannelOptions(surveyCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                }
            )
            : Channel.CreateBounded<byte[]>(
                new BoundedChannelOptions(viewerCapacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                },
                _ =>
                {
                    subscription?.CountDrop();
                    Tally();
                }
            );

        subscription = new SessionSubscription(kind, channel);

        lock (gate)
        {
            if (closed)
            {
                if (!acceptClosed)
                {
                    return null;
                }

                subscription.IsDisconnected = true;
                subscription.Channel.Writer.TryComplete(closedBecause);

                return subscription;
            }

            if (subscriptions.Count >= limit)
            {
                return null;
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
                report?.Invoke(error);
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
                Tally();

                return;

            default:
                subscription.IsDisconnected = true;
                subscription.IsTruncated = true;
                subscription.CountDrop();
                Tally();
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
                return Delivery.Abandoned;
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
