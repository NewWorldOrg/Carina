using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Notifications;

public sealed record ReservationOutcomeNoticeSource(
    Guid OutcomeId,
    ReservationOutcomeKind Kind,
    string ProgrammeName,
    string? Station,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime OccurredAt,
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

    public override IEnumerable<NoticeHappening> Happenings()
    {
        if (KindsByOutcome.TryGetValue(Kind, out NoticeKind kind))
        {
            yield return new NoticeHappening(kind, OutcomeId, OccurredAt, this);
        }
    }
}
