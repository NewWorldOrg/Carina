using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

public sealed class ReservationPlanningDomainService(
    IProgrammeRepository programmes,
    IReservationRepository reservations,
    TimeProvider clock)
{
    public async Task<ReservationPlanningConsidered> ConsiderAsync(
        ProgrammeId programme,
        DateTime startsAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programme);

        DateTime at = clock.GetUtcNow().UtcDateTime;

        if (await programmes.FindAsync(programme, cancellationToken) is not { } held || held.StartsAt != startsAt)
        {
            return ReservationPlanningConsidered.Refused(ReservationPlanningRefusal.NoSuchProgramme, at);
        }

        if (held.IsShadow)
        {
            return ReservationPlanningConsidered.Refused(ReservationPlanningRefusal.Shadow, at);
        }

        BroadcastGroupResolver groups = BroadcastGroupResolver.Of(await programmes.ListGroupedAsync(cancellationToken));
        BroadcastResolution resolution = groups.Resolve(held, at);

        if (PrimaryOfMoved(resolution) is { } primary)
        {
            return ReservationPlanningConsidered.Refused(ReservationPlanningRefusal.MovedDuplicate, at, primary);
        }

        var reference = new ProgrammeRef(held.NetworkId, held.ServiceId, held.EventId, held.StartsAt);

        if (await reservations.FindByProgrammeAsync(reference, cancellationToken) is { } already)
        {
            return ReservationPlanningConsidered.Refused(ReservationPlanningRefusal.AlreadyReserved, at, already: already);
        }

        return new ReservationPlanningConsidered(held, resolution, null, null, null, at);
    }

    public async Task<bool> NeedsReservingAlongsideAsync(
        BroadcastTarget target,
        ProgrammeId asked,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(asked);

        return !target.Programme.Id.Equals(asked)
               && await reservations.FindByProgrammeAsync(target.Reference, cancellationToken) is null;
    }

    public async Task<Programme?> PrimaryOfMovedAsync(ProgrammeId programme, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programme);

        if (await programmes.FindAsync(programme, cancellationToken) is not { } held)
        {
            return null;
        }

        BroadcastGroupResolver groups = BroadcastGroupResolver.Of(await programmes.ListGroupedAsync(cancellationToken));

        return PrimaryOfMoved(groups.Resolve(held, clock.GetUtcNow().UtcDateTime));
    }

    private static Programme? PrimaryOfMoved(BroadcastResolution resolution)
        => resolution.Exclusion is BroadcastExclusion.Moved && resolution.Targets.Count > 0
            ? resolution.Targets[0].Programme
            : null;
}
