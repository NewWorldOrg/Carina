using Carina.Domain.Recordings;

namespace Carina.Domain.Reservations;

public static class ReservationOutcomeJudgement
{
    public static ReservationOutcomeKind? Of(
        Reservation reservation,
        bool recorded,
        bool leftScrambled,
        TimeSpan grace,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        if (reservation.RecordingOutcome is RecordingOutcome.Failed or RecordingOutcome.Truncated
            || (reservation.RecordingOutcome is RecordingOutcome.Complete && leftScrambled))
        {
            return ReservationOutcomeKind.RecordingFailure;
        }

        if (reservation.RecordingOutcome is not null)
        {
            return null;
        }

        if (reservation.IsPinned && recorded)
        {
            return null;
        }

        if (at < reservation.EffectiveStartAt + grace || at < reservation.EffectiveEndAt)
        {
            return null;
        }

        return reservation.State switch
        {
            ReservationState.Scheduled => ReservationOutcomeKind.Missed,
            ReservationState.Conflict => ReservationOutcomeKind.Competing,
            _ => null,
        };
    }
}
