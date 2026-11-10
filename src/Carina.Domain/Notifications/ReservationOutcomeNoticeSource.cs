using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Notifications;

public sealed record ReservationOutcomeNoticeSource(
    Guid OutcomeId,
    Guid ReservationId,
    ReservationOutcomeKind Kind,
    string ProgrammeName,
    string? Station,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime OccurredAt,
    RecordingOutcome? Recorded,
    IReadOnlyList<RecordingFault> Faults,
    RetryGiveUp? GaveUpBecause,
    IReadOnlyList<string> RecordedInstead) : NoticeSource
{
    private static readonly IReadOnlyDictionary<ReservationOutcomeKind, NoticeKind> KindsByOutcome =
        new Dictionary<ReservationOutcomeKind, NoticeKind>
        {
            [ReservationOutcomeKind.Competing] = NoticeKind.ReservationCompeting,
            [ReservationOutcomeKind.Missed] = NoticeKind.ReservationMissed,
            [ReservationOutcomeKind.RecordingFailure] = NoticeKind.ReservationMissed,
            [ReservationOutcomeKind.GaveUpRetrying] = NoticeKind.ReservationMissed,
            [ReservationOutcomeKind.ProgrammeMoved] = NoticeKind.ProgrammeMoved,
            [ReservationOutcomeKind.ProgrammeGone] = NoticeKind.ProgrammeMoved,
        };

    private bool IsLeftToTheRecording
        => Kind is ReservationOutcomeKind.RecordingFailure && Recorded is RecordingOutcome.Truncated;

    public override IEnumerable<NoticeHappening> Happenings()
    {
        if (!IsLeftToTheRecording && KindsByOutcome.TryGetValue(Kind, out NoticeKind kind))
        {
            yield return new NoticeHappening(kind, kind is NoticeKind.ReservationMissed ? ReservationId : OutcomeId, OccurredAt, this);
        }
    }
}
