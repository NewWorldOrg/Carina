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
}

public static class NoticeKinds
{
    public static NoticeKind Named(NoticeKind kind)
        => Enum.IsDefined(kind)
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "A notice is of one of the kinds the ledger holds.");
}
