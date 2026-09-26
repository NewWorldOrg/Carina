using Carina.Domain.Recordings;

namespace Carina.Domain.Reservations;

public static class ReservationOutcomeJudgement
{
    public static ReservationOutcomeKind? Of(
        Reservation reservation,
        bool recorded,
        TimeSpan grace,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        if (reservation.RecordingOutcome is RecordingOutcome.Failed or RecordingOutcome.Truncated)
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
