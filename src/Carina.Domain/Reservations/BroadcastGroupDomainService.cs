using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

/// <summary>
/// Decides how a reservation still to begin is held against the relayed and moved broadcasts the guide links, looking
/// up the reservations already standing and the programmes the guide still holds.
/// </summary>
public sealed class BroadcastGroupDomainService(
    IReservationRepository reservations,
    IProgrammeRepository programmes)
{
    /// <summary>
    /// A reservation on the listing that stands for its broadcast takes that listing's key and role. One on a listing
    /// a moved broadcast suppresses moves onto the primary listing when nothing stands there; it steps aside as the same
    /// broadcast when a reservation there is recorded, secured or contended; and it keeps its listing as the suppressed
    /// one when the reservation there was cancelled or missed.
    /// </summary>
    public async Task<BroadcastGroupPlacing> PlacingAsync(
        Reservation reservation,
        BroadcastTarget own,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(own);

        if (own.Programme.Id.Equals(reservation.Programme.Id))
        {
            return BroadcastGroupPlacing.Regrouping(own.Key, own.Role);
        }

        if (await reservations.FindByProgrammeAsync(own.Reference, cancellationToken) is not { } there)
        {
            return BroadcastGroupPlacing.Retargeting;
        }

        return there.RecordingOutcome is not null
               || there.State is ReservationState.Scheduled or ReservationState.Conflict
            ? BroadcastGroupPlacing.SteppingAside
            : BroadcastGroupPlacing.Regrouping(own.Key, BroadcastGroupRole.MovementSuppressed);
    }

    /// <summary>
    /// Whether a reservation no group places any more goes back to standing alone: it was grouped, and the guide still
    /// holds its programme.
    /// </summary>
    public async Task<bool> FallsBackToStandingAloneAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        return reservation.BroadcastGroupRole is not BroadcastGroupRole.Standalone
               && await programmes.FindAsync(reservation.Programme.Id, cancellationToken) is not null;
    }

    /// <summary>
    /// Whether another segment of a relayed broadcast still has no reservation, and so is reserved after the example
    /// of the one standing. A segment a person cancelled keeps its cancelled reservation and is not made again.
    /// </summary>
    public async Task<bool> NeedsReservingBesideAsync(BroadcastTarget sibling, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sibling);

        return await reservations.FindByProgrammeAsync(sibling.Reference, cancellationToken) is null;
    }
}
