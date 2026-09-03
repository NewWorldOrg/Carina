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

        if (reservation.RecordingOutcome is RecordingOutcome.Failed)
        {
            return ReservationOutcomeKind.RecordingFailure;
        }

        if (reservation.RecordingOutcome is not null)
        {
            return null;
        }

        // A claim with a recording behind it is that recording's to settle, and it settles it by
        // writing the outcome above. A claim with no recording behind it has nothing that will ever
        // do so, which is the one case a claimed reservation is judged here.
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
