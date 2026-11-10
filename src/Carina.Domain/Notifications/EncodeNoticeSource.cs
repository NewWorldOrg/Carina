using Carina.Domain.Encodings;

namespace Carina.Domain.Notifications;

public sealed record EncodeNoticeSource(
    Guid JobId,
    Guid RecordingId,
    string ProgrammeName,
    EncodeCodec? Codec,
    EncodeJobStatus Status,
    DateTime? StartedAt,
    DateTime? EndedAt,
    EncodeFailure? Failure) : NoticeSource
{
    private static readonly IReadOnlyDictionary<EncodeJobStatus, NoticeKind> KindsByEnding =
        new Dictionary<EncodeJobStatus, NoticeKind>
        {
            [EncodeJobStatus.Completed] = NoticeKind.EncodeCompleted,
            [EncodeJobStatus.Failed] = NoticeKind.EncodeFailed,
        };

    public override IEnumerable<NoticeHappening> Happenings()
    {
        if (StartedAt is { } startedAt)
        {
            yield return new NoticeHappening(NoticeKind.EncodeStarted, JobId, startedAt, this);
        }

        if (EndedAt is { } endedAt && KindsByEnding.TryGetValue(Status, out NoticeKind kind))
        {
            yield return new NoticeHappening(kind, JobId, endedAt, this);
        }
    }
}
