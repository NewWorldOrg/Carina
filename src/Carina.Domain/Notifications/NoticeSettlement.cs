namespace Carina.Domain.Notifications;

/// <summary>
/// Where one attempt to send a notice left its delivery.
/// </summary>
public enum NoticeSettlement
{
    Sent = 1,

    FailedToTryAgain = 2,

    FailedForGood = 3,

    GivenUp = 4,
}
