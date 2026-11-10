namespace Carina.Domain.Notifications;

public enum NoticeTone
{
    Good = 1,

    Informative = 2,

    Caution = 3,

    Failure = 4,
}

public static class NoticeTones
{
    private static readonly IReadOnlyDictionary<NoticeKind, NoticeTone> ByKind = new Dictionary<NoticeKind, NoticeTone>
    {
        [NoticeKind.RecordingStarted] = NoticeTone.Informative,
        [NoticeKind.RecordingCompleted] = NoticeTone.Good,
        [NoticeKind.RecordingTruncated] = NoticeTone.Caution,
        [NoticeKind.RecordingFailed] = NoticeTone.Failure,
        [NoticeKind.EncodeStarted] = NoticeTone.Informative,
        [NoticeKind.EncodeCompleted] = NoticeTone.Good,
        [NoticeKind.EncodeFailed] = NoticeTone.Failure,
        [NoticeKind.ReservationCompeting] = NoticeTone.Caution,
        [NoticeKind.ReservationMissed] = NoticeTone.Failure,
        [NoticeKind.ProgrammeMoved] = NoticeTone.Caution,
        [NoticeKind.QualityIncidentDetected] = NoticeTone.Caution,
        [NoticeKind.QualityIncidentResolved] = NoticeTone.Good,
        [NoticeKind.DeviceDisconnected] = NoticeTone.Failure,
        [NoticeKind.ProgrammeStarting] = NoticeTone.Informative,
        [NoticeKind.WatchedProgrammeMoved] = NoticeTone.Caution,
        [NoticeKind.WatchedProgrammeGone] = NoticeTone.Caution,
    };

    public static NoticeTone Of(NoticeKind kind) => ByKind[NoticeKinds.Named(kind)];

    public static NoticeTone Named(NoticeTone tone)
        => Enum.IsDefined(tone)
            ? tone
            : throw new ArgumentOutOfRangeException(nameof(tone), tone, "A notice is told in one of four tones.");
}
