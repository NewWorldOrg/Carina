using System.Collections.Concurrent;

using Carina.Domain.Notifications;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// Where every notice leaves from, the loop's and a trial alike: one at a time for a destination, counted against the
/// most a destination takes in a window, and held back for as long as the other end asked to be left alone.
/// </summary>
public sealed class NoticeDispatch(NoticeLine line, NotificationDeliverySettings settings, TimeProvider clock)
{
    private readonly ConcurrentDictionary<NotificationDestinationId, Lane> lanes = new();

    public bool HasRoomFor(NotificationDestinationId destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return LaneOf(destination).HasRoom(Now(), settings);
    }

    public async Task<NoticeAnswer> SendAsync(
        NotificationDestinationId destination,
        WebhookUrl webhook,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(webhook);
        ArgumentNullException.ThrowIfNull(payload);

        Lane lane = LaneOf(destination);

        await lane.Gate.WaitAsync(cancellationToken);

        try
        {
            lane.Sending(Now(), settings.Window);

            NoticeAnswer answer = await line.SendAsync(webhook, payload, cancellationToken);

            lane.Heard(answer, Now());

            return answer;
        }
        finally
        {
            lane.Gate.Release();
        }
    }

    public void Forget(NotificationDestinationId destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        lanes.TryRemove(destination, out _);
    }

    private Lane LaneOf(NotificationDestinationId destination) => lanes.GetOrAdd(destination, _ => new Lane());

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private sealed class Lane
    {
        private readonly Lock guard = new();

        private readonly Queue<DateTime> sent = new();

        private DateTime heldUntil = DateTime.MinValue;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public bool HasRoom(DateTime now, NotificationDeliverySettings settings)
        {
            lock (guard)
            {
                Trim(now - settings.Window);

                return now >= heldUntil && sent.Count < settings.MostInAWindow;
            }
        }

        public void Sending(DateTime now, TimeSpan window)
        {
            lock (guard)
            {
                Trim(now - window);
                sent.Enqueue(now);
            }
        }

        public void Heard(NoticeAnswer answer, DateTime now)
        {
            if (answer.Failure is not NoticeFailure.RateLimited || answer.Asked is not { } asked)
            {
                return;
            }

            lock (guard)
            {
                heldUntil = now + (asked > NoticeRetry.LongestWait ? NoticeRetry.LongestWait : asked);
            }
        }

        private void Trim(DateTime since)
        {
            while (sent.TryPeek(out DateTime oldest) && oldest <= since)
            {
                sent.Dequeue();
            }
        }
    }
}
