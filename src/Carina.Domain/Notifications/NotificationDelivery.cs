using Carina.Domain.Base;

namespace Carina.Domain.Notifications;

/// <summary>
/// One notice on its way to one destination: waiting to be sent, sent, waiting to be tried again after a failure, or
/// given up.
/// </summary>
public sealed class NotificationDelivery
{
    public const int MostListed = 50;

    private NotificationDelivery()
    {
    }

    public NotificationDeliveryId Id { get; private set; } = null!;

    public NoticeId NoticeId { get; private set; } = null!;

    public NotificationDestinationId DestinationId { get; private set; } = null!;

    public NotificationDeliveryState State { get; private set; }

    public int Attempts { get; private set; }

    public DateTime? NextAttemptAt { get; private set; }

    public DateTime? SentAt { get; private set; }

    public NoticeFailure? LastFailure { get; private set; }

    public DateTime QueuedAt { get; private set; }

    public bool IsWaiting => State is NotificationDeliveryState.Queued or NotificationDeliveryState.Failed;

    public static int Listed(int? perPage) => ListingGuards.Clamped(perPage, MostListed, MostListed);

    public static NotificationDelivery Queue(
        NotificationDeliveryId id,
        NoticeId notice,
        NotificationDestinationId destination,
        DateTime at)
        => Rehydrate(id, notice, destination, NotificationDeliveryState.Queued, 0, at, null, null, at);

    public static NotificationDelivery Rehydrate(
        NotificationDeliveryId id,
        NoticeId notice,
        NotificationDestinationId destination,
        NotificationDeliveryState state,
        int attempts,
        DateTime? nextAttemptAt,
        DateTime? sentAt,
        NoticeFailure? lastFailure,
        DateTime queuedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(destination);

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "A delivery stands in one of four states.");
        }

        if (attempts < 0 || attempts > NoticeRetry.MostAttempts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attempts),
                attempts,
                $"A delivery is tried at most {NoticeRetry.MostAttempts} times.");
        }

        NotificationDelivery delivery = new()
        {
            Id = id,
            NoticeId = notice,
            DestinationId = destination,
            State = state,
            Attempts = attempts,
            NextAttemptAt = UtcTimes.Optional(nextAttemptAt, nameof(nextAttemptAt)),
            SentAt = UtcTimes.Optional(sentAt, nameof(sentAt)),
            LastFailure = lastFailure is { } failure ? NoticeFailures.Named(failure) : null,
            QueuedAt = UtcTimes.Required(queuedAt, nameof(queuedAt)),
        };

        if (!delivery.StandsWhereItsOwnFieldsPutIt())
        {
            throw new ArgumentException("A delivery stands where its own times and its last failure put it.", nameof(state));
        }

        return delivery;
    }

    public void Deliver(DateTime at)
    {
        RefuseUnlessWaiting();

        State = NotificationDeliveryState.Sent;
        Attempts++;
        SentAt = UtcTimes.Required(at, nameof(at));
        NextAttemptAt = null;
        LastFailure = null;
    }

    /// <summary>
    /// Notes an attempt that failed in a way worth another try. The delivery waits for the next attempt, or is given
    /// up when none is left.
    /// </summary>
    public void Fail(NoticeFailure failure, DateTime at, TimeSpan? asked = null)
    {
        RefuseUnlessWaiting();

        if (!NoticeFailures.IsWorthAnotherTry(failure))
        {
            throw new ArgumentException($"A delivery that failed as {failure} is given up, not tried again.", nameof(failure));
        }

        DateTime failedAt = UtcTimes.Required(at, nameof(at));
        TimeSpan? wait = NoticeRetry.After(Attempts + 1, asked);

        Attempts++;
        LastFailure = failure;
        State = wait is null ? NotificationDeliveryState.GivenUp : NotificationDeliveryState.Failed;
        NextAttemptAt = wait is { } waiting ? failedAt + waiting : null;
    }

    public void GiveUp(NoticeFailure failure)
    {
        RefuseUnlessWaiting();

        LastFailure = NoticeFailures.Named(failure);
        State = NotificationDeliveryState.GivenUp;
        NextAttemptAt = null;
    }

    private bool StandsWhereItsOwnFieldsPutIt()
        => State switch
        {
            NotificationDeliveryState.Queued => Attempts is 0 && NextAttemptAt is not null && SentAt is null && LastFailure is null,
            NotificationDeliveryState.Failed => Attempts > 0 && NextAttemptAt is not null && SentAt is null && LastFailure is not null,
            NotificationDeliveryState.Sent => Attempts > 0 && NextAttemptAt is null && SentAt is not null && LastFailure is null,
            _ => NextAttemptAt is null && SentAt is null && LastFailure is not null,
        };

    private void RefuseUnlessWaiting()
    {
        if (!IsWaiting)
        {
            throw new InvalidOperationException($"This delivery is {State} and is not moved again.");
        }
    }
}
