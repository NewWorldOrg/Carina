using Carina.Domain.Recordings;

namespace Carina.Domain.Reservations;

public static class ReservationOutcomeJudgement
{
    public static ReservationOutcomeKind? Of(Reservation reservation, TimeSpan grace, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        if (reservation.RecordingOutcome is RecordingOutcome.Failed)
        {
            return ReservationOutcomeKind.RecordingFailure;
        }

        if (reservation.IsPinned)
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
