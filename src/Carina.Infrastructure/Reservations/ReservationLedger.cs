using Carina.Domain.Reservations;

namespace Carina.Infrastructure.Reservations;

/// <summary>
/// One line per reservation per classification, and no more. A broadcast that slips again and again
/// is one broadcast that moved, and one that goes away and comes back and goes away again is one
/// broadcast that went: the reservation itself carries where it stands now, so a second line would
/// say nothing the first does not and would bury the ledger under a schedule that shifts most days.
/// </summary>
internal static class ReservationLedger
{
    public static async Task<bool> WriteOnceAsync(
        IReservationOutcomeRepository outcomes,
        Reservation reservation,
        ReservationOutcomeKind kind,
        DateTime at,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ReservationOutcome> already =
            await outcomes.ListForReservationAsync(reservation.Id, cancellationToken);

        if (already.Any(outcome => outcome.Kind == kind))
        {
            return false;
        }

        await outcomes.AddAsync(
            ReservationOutcome.Record(
                ReservationOutcomeId.New(),
                reservation,
                kind,
                null,
                null,
                [],
                [],
                at),
            cancellationToken);

        return true;
    }
}
