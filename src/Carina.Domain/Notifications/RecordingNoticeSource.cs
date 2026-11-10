using Carina.Domain.Recordings;

namespace Carina.Domain.Notifications;

public sealed record RecordingNoticeSource(
    Guid RecordingId,
    bool Reserved,
    string ProgrammeName,
    string? Station,
    DateTime ProgrammeStartsAt,
    DateTime EndsAt,
    DateTime StartedAt,
    DateTime? SettledAt,
    RecordingOutcome? Outcome,
    long MissedMs,
    IReadOnlyList<RecordingFault> Faults) : NoticeSource
{
    private static readonly IReadOnlyDictionary<RecordingOutcome, NoticeKind> KindsBySettledOutcome =
        new Dictionary<RecordingOutcome, NoticeKind>
        {
            [RecordingOutcome.Complete] = NoticeKind.RecordingCompleted,
            [RecordingOutcome.Truncated] = NoticeKind.RecordingTruncated,
        };

    public override IEnumerable<NoticeHappening> Happenings()
    {
        yield return new NoticeHappening(NoticeKind.RecordingStarted, RecordingId, StartedAt, this);

        if (Settled() is { } settled)
        {
            yield return settled;
        }
    }

    private bool EndedInFailure(RecordingOutcome outcome)
        => outcome is RecordingOutcome.Failed
            || (outcome is RecordingOutcome.Complete && Faults.Contains(RecordingFault.ScramblingUnresolved));

    private NoticeHappening? Settled()
    {
        if (Outcome is not { } outcome || SettledAt is not { } settledAt)
        {
            return null;
        }

        if (EndedInFailure(outcome))
        {
            return Reserved ? null : new NoticeHappening(NoticeKind.RecordingFailed, RecordingId, settledAt, this);
        }

        return KindsBySettledOutcome.TryGetValue(outcome, out NoticeKind kind)
            ? new NoticeHappening(kind, RecordingId, settledAt, this)
            : null;
    }
}
