using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

using Carina.Contracts;
using Carina.Driver.Recording;

namespace Carina.Driver.Sessions;

public enum SubscriberKind
{
    Viewer,
    Piggyback,
    Survey,
    Recording,
}

public sealed record ViewerLoss(int Wire, long ChunksDropped, bool StillReading);

public sealed class SessionSubscription
{
    private long droppedChunks;

    internal SessionSubscription(SubscriberKind kind, Channel<byte[]> channel)
    {
        Kind = kind;
        Channel = channel;
    }

    public int Wire { get; internal set; }

    internal Channel<byte[]> Channel { get; }

    public SubscriberKind Kind { get; }

    public ChannelReader<byte[]> Reader => Channel.Reader;

    public bool IsDisconnected { get; internal set; }

    public bool IsTruncated { get; internal set; }

    public bool FellBehind { get; internal set; }

    public SessionStopReason EndedWith { get; internal set; }

    public long DroppedChunks => Interlocked.Read(ref droppedChunks);

    internal long CountDrop() => Interlocked.Increment(ref droppedChunks);
}

public sealed class SessionBroadcaster(
    int viewerCapacity = SessionBroadcaster.DefaultViewerCapacity,
    int surveyCapacity = SessionBroadcaster.DefaultSurveyCapacity,
    TimeSpan? surveyBlockLimit = null,
    Action<Exception>? report = null,
    int subscriberLimit = SessionBroadcaster.DefaultSubscriberLimit,
    int recordingCapacity = SessionBroadcaster.DefaultRecordingCapacity,
    TimeSpan? recordingBlockLimit = null,
    Action<ViewerLoss>? viewerFallingBehind = null
) : IDisposable
{
    public const int DefaultViewerCapacity = 64;
    public const int DefaultSurveyCapacity = 256;
    public const int DefaultSubscriberLimit = 8;

    public const int DefaultRecordingCapacity =
        (int)(RecordingWriter.FlushInterval / TunerSession.DefaultChunkSize) + 1;

    public static readonly TimeSpan DefaultSurveyBlockLimit = TimeSpan.FromSeconds(5);

    private enum Delivery
    {
        Delivered,
        Abandoned,
        Refused,
    }

    private readonly ConcurrentDictionary<SessionSubscription, byte> subscriptions = [];
    private readonly TimeSpan blockLimit = surveyBlockLimit ?? DefaultSurveyBlockLimit;
    private readonly TimeSpan recordingBlock = recordingBlockLimit ?? TimeSpan.Zero;
    private readonly Lock gate = new();
    private readonly List<ViewerLoss> viewersGone = [];

    private bool closed;
    private Exception? closedBecause;
    private SessionStopReason closedReason;
    private long droppedChunks;
    private int wires;

    public int SubscriberCount => subscriptions.Count;

    public TimeSpan RecordingWait => recordingBlock;

    public IReadOnlyList<SubscriberKind> KindsInUse =>
        [.. subscriptions.Keys.Select(subscription => subscription.Kind)];

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

    public IReadOnlyList<ViewerLoss> ViewerLosses
    {
        get
        {
            lock (gate)
            {
                return
                [
                    .. viewersGone
                        .Concat(subscriptions
                            .Keys.Where(subscription => !Waits(subscription.Kind))
                            .Select(subscription => new ViewerLoss(
                                subscription.Wire,
                                subscription.DroppedChunks,
                                StillReading: true
                            )))
                        .OrderBy(loss => loss.Wire),
                ];
            }
        }
    }

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

        Channel<byte[]> channel = Waits(kind)
            ? Channel.CreateBounded<byte[]>(
                new BoundedChannelOptions(
                    kind is SubscriberKind.Recording ? recordingCapacity : surveyCapacity
                )
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
                    if (subscription?.CountDrop() is { } lost && Announces(lost))
                    {
                        viewerFallingBehind?.Invoke(
                            new ViewerLoss(subscription.Wire, lost, StillReading: true)
                        );
                    }

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
                subscription.EndedWith = closedReason;
                subscription.Channel.Writer.TryComplete(closedBecause);

                return subscription;
            }

            if (subscriptions.Count >= limit)
            {
                return null;
            }

            if (!Waits(kind))
            {
                subscription.Wire = ++wires;
            }

            subscriptions[subscription] = 0;
        }

        return subscription;
    }

    public void Unsubscribe(
        SessionSubscription subscription,
        Exception? because = null,
        SessionStopReason endedWith = SessionStopReason.Unspecified
    )
    {
        if (!subscriptions.TryRemove(subscription, out _))
        {
            return;
        }

        subscription.EndedWith = endedWith;
        subscription.Channel.Writer.TryComplete(because);

        if (Waits(subscription.Kind) || subscription.DroppedChunks is 0)
        {
            return;
        }

        lock (gate)
        {
            viewersGone.Add(
                new ViewerLoss(subscription.Wire, subscription.DroppedChunks, StillReading: false)
            );
        }
    }

    public void Publish(ReadOnlySpan<byte> chunk, CancellationToken cancellationToken = default)
    {
        if (subscriptions.IsEmpty)
        {
            return;
        }

        byte[] copy = chunk.ToArray();

        foreach (KeyValuePair<SessionSubscription, byte> entry in subscriptions)
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

    public void Close(Exception? because, SessionStopReason endedWith = SessionStopReason.Unspecified)
    {
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            closed = true;
            closedBecause = because;
            closedReason = endedWith;
        }

        foreach (KeyValuePair<SessionSubscription, byte> entry in subscriptions)
        {
            Unsubscribe(entry.Key, because ?? Truncation(entry.Key), endedWith);
        }
    }

    private void Deliver(
        SessionSubscription subscription,
        byte[] chunk,
        CancellationToken cancellationToken
    )
    {
        if (!Waits(subscription.Kind))
        {
            subscription.Channel.Writer.TryWrite(chunk);

            return;
        }

        TimeSpan limit = LimitFor(subscription.Kind);

        switch (DeliverWithinLimit(subscription, chunk, limit, cancellationToken))
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
                subscription.FellBehind = true;
                subscription.CountDrop();
                Tally();
                Unsubscribe(
                    subscription,
                    TooSlow(limit),
                    subscription.Kind is SubscriberKind.Recording
                        ? SessionStopReason.RecordingFailed
                        : SessionStopReason.Unspecified
                );

                return;
        }
    }

    private static bool Waits(SubscriberKind kind) =>
        kind is SubscriberKind.Survey or SubscriberKind.Recording;

    private static bool Announces(long lost)
    {
        for (long step = 1; step <= lost; step *= 10)
        {
            if (step == lost)
            {
                return true;
            }
        }

        return false;
    }

    private TimeSpan LimitFor(SubscriberKind kind) =>
        kind is SubscriberKind.Recording ? recordingBlock : blockLimit;

    private static Delivery DeliverWithinLimit(
        SessionSubscription subscription,
        byte[] chunk,
        TimeSpan limit,
        CancellationToken cancellationToken
    )
    {
        long start = Stopwatch.GetTimestamp();

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

            TimeSpan left = limit - Stopwatch.GetElapsedTime(start);
            if (left <= TimeSpan.Zero)
            {
                return Delivery.Refused;
            }

            Task<bool> room = subscription.Channel.Writer.WaitToWriteAsync().AsTask();

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

    private static Exception TooSlow(TimeSpan limit) =>
        limit <= TimeSpan.Zero
            ? new IOException(
                "The subscriber's buffer filled up, and this session never waits for a subscriber, so it was disconnected."
            )
            : new TimeoutException(
                $"The subscriber did not take the stream within {limit}, so it was disconnected."
            );

    private static Exception? Truncation(SessionSubscription subscription) =>
        subscription.IsTruncated
            ? new IOException("The stream ended before every chunk reached this subscriber.")
            : null;
}
