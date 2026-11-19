namespace Carina.Domain.Notifications;

public enum NoticeSettlement
{
    Sent = 1,

    FailedToTryAgain = 2,

    FailedForGood = 3,

    GivenUp = 4,
}
