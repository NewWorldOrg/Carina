namespace Carina.Domain.Reservations;

/// <summary>
/// Thrown when a reservation was changed in the ledger after the writer read it.
/// </summary>
public sealed class ReservationMovedMeanwhileException(IReadOnlyList<ReservationId> reservations)
    : InvalidOperationException(
        $"Reservation(s) {string.Join(", ", reservations?.Select(id => id.Value) ?? [])} changed in the ledger "
        + "after they were read, so what was written from that reading is dropped.")
{
    public IReadOnlyList<ReservationId> Reservations { get; } =
        reservations ?? throw new ArgumentNullException(nameof(reservations));
}
