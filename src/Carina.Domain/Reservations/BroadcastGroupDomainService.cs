using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

public sealed class BroadcastGroupDomainService(
    IReservationRepository reservations,
    IProgrammeRepository programmes)
{
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

    public async Task<bool> FallsBackToStandingAloneAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        return reservation.BroadcastGroupRole is not BroadcastGroupRole.Standalone
               && await programmes.FindAsync(reservation.Programme.Id, cancellationToken) is not null;
    }

    public async Task<bool> NeedsReservingBesideAsync(BroadcastTarget sibling, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sibling);

        return await reservations.FindByProgrammeAsync(sibling.Reference, cancellationToken) is null;
    }
}
