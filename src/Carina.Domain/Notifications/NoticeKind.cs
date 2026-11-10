namespace Carina.Domain.Notifications;

public enum NoticeKind
{
    RecordingStarted = 1,

    RecordingCompleted = 2,

    RecordingTruncated = 3,

    RecordingFailed = 4,

    EncodeStarted = 5,

    EncodeCompleted = 6,

    EncodeFailed = 7,

    ReservationCompeting = 8,

    ReservationMissed = 9,

    ProgrammeMoved = 10,

    QualityIncidentDetected = 11,

    QualityIncidentResolved = 12,

    DeviceDisconnected = 13,

    ProgrammeStarting = 14,

    WatchedProgrammeMoved = 15,

    WatchedProgrammeGone = 16,
}

public static class NoticeKinds
{
    /// <summary>
    /// The kinds told of one programme somebody asked to be told of, to the destinations asked for with it.
    /// </summary>
    public static IReadOnlyList<NoticeKind> OfAWatchedProgramme { get; } =
    [
        NoticeKind.ProgrammeStarting,
        NoticeKind.WatchedProgrammeMoved,
        NoticeKind.WatchedProgrammeGone,
    ];

    /// <summary>
    /// The kinds a set of rules may list.
    /// </summary>
    public static IReadOnlyList<NoticeKind> ChosenByRules { get; } =
    [
        .. Enum.GetValues<NoticeKind>().Except(OfAWatchedProgramme),
    ];

    public static NoticeKind Named(NoticeKind kind)
        => Enum.IsDefined(kind)
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "A notice is of one of the kinds the ledger holds.");

    public static NoticeKind ChosenByARule(NoticeKind kind)
        => ChosenByRules.Contains(kind)
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "A set of rules lists kinds that rules choose between.");
}
