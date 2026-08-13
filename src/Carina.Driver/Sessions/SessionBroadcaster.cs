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

    private readonly ConcurrentDictionary<SessionSubscription, byte> subscriptions = [];
    private readonly TimeSpan blockLimit = surveyBlockLimit ?? DefaultSurveyBlockLimit;
    private readonly Lock gate = new();

    private bool closed;
    private Exception? closedBecause;

    public int SubscriberCount => subscriptions.Count;

    public SessionSubscription Subscribe(SubscriberKind kind)
    {
        var options = kind is SubscriberKind.Survey
            ? new BoundedChannelOptions(surveyCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
            }
            : new BoundedChannelOptions(viewerCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = true,
            };

        var subscription = new SessionSubscription(kind, Channel.CreateBounded<byte[]>(options));

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
            var subscription = entry.Key;

            if (subscription.Kind is not SubscriberKind.Survey)
            {
                if (subscription.Channel.Reader.Count >= viewerCapacity)
                {
                    subscription.CountDrop();
                }

                subscription.Channel.Writer.TryWrite(copy);
                continue;
            }

            if (DeliverWithinLimit(subscription, copy, cancellationToken))
            {
                continue;
            }

            subscription.IsDisconnected = true;
            Unsubscribe(
                subscription,
                new TimeoutException(
                    $"The subscriber did not take the stream within {blockLimit}, so it was disconnected."
                )
            );
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
            Unsubscribe(entry.Key, because);
        }
    }

    private bool DeliverWithinLimit(
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
                return true;
            }

            var left = blockLimit - Stopwatch.GetElapsedTime(start);
            if (left <= TimeSpan.Zero)
            {
                return false;
            }

            var room = subscription.Channel.Writer.WaitToWriteAsync().AsTask();

            try
            {
                if (!room.Wait((int)left.TotalMilliseconds, cancellationToken) || !room.Result)
                {
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                return true;
            }
        }
    }
}
