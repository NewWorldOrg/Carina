namespace Carina.Domain.Notifications;

public enum NoticeFailure
{
    TimedOut = 1,

    RateLimited = 2,

    ServerFailed = 3,

    Unreachable = 4,

    Rejected = 5,

    DestinationDisabled = 6,

    Unreadable = 7,

    TooOld = 8,

    TlsFailed = 9,
}

public static class NoticeFailures
{
    private static readonly IReadOnlySet<NoticeFailure> WorthAnotherTry = new HashSet<NoticeFailure>
    {
        NoticeFailure.TimedOut,
        NoticeFailure.RateLimited,
        NoticeFailure.ServerFailed,
        NoticeFailure.Unreachable,
        NoticeFailure.TlsFailed,
    };

    public static NoticeFailure Named(NoticeFailure failure)
        => Enum.IsDefined(failure)
            ? failure
            : throw new ArgumentOutOfRangeException(
                nameof(failure),
                failure,
                "A delivery fails for one of the reasons the ledger holds.");

    public static bool IsWorthAnotherTry(NoticeFailure failure) => WorthAnotherTry.Contains(Named(failure));
}
