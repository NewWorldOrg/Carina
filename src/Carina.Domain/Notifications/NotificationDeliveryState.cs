namespace Carina.Domain.Notifications;

public enum NotificationDeliveryState
{
    Queued = 1,

    Sent = 2,

    Failed = 3,

    GivenUp = 4,
}
