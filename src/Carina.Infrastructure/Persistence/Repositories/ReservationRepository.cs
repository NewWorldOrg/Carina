using Carina.Domain.Reservations;
using Carina.Domain.Rules;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class ReservationRepository(CarinaDbContext context) : IReservationRepository
{
    public async Task<Reservation?> FindAsync(ReservationId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<Reservation>()
            .FirstOrDefaultAsync(reservation => reservation.Id == id, cancellationToken);
    }

    public async Task<Reservation?> FindByProgrammeAsync(
        ProgrammeRef programme,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programme);

        return await context.Set<Reservation>()
            .FirstOrDefaultAsync(
                reservation => reservation.NetworkId == programme.NetworkId
                               && reservation.ServiceId == programme.ServiceId
                               && reservation.EventId == programme.EventId
                               && reservation.ProgrammeStartsAt == programme.StartsAt,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Reservation>> ListPendingAsync(
        ReservationWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        return await context.Set<Reservation>()
            .Where(reservation => reservation.RecordingOutcome == null)
            .Where(reservation => reservation.State == ReservationState.Scheduled
                                  || reservation.State == ReservationState.Conflict)
            .Where(reservation => reservation.StartedAt != null
                                  || (reservation.EndAt >= window.From && reservation.StartAt <= window.To))
            .OrderBy(reservation => reservation.StartAt)
            .ThenBy(reservation => reservation.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Reservation>> ListForRuleAsync(
        RuleId ruleId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ruleId);

        return await context.Set<Reservation>()
            .Where(reservation => reservation.RuleId == ruleId)
            .OrderBy(reservation => reservation.StartAt)
            .ThenBy(reservation => reservation.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Reservation>> ListForBroadcastGroupAsync(
        BroadcastGroupKey key,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        return await context.Set<Reservation>()
            .Where(reservation => reservation.BroadcastGroupKey == key)
            .OrderBy(reservation => reservation.StartAt)
            .ThenBy(reservation => reservation.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        context.Add(reservation);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        context.Update(reservation);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAllAsync(IReadOnlyList<Reservation> reservations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservations);

        foreach (Reservation reservation in reservations)
        {
            context.Update(reservation);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task WithdrawAsync(IReadOnlyList<Reservation> reservations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservations);

        context.RemoveRange(reservations);

        await context.SaveChangesAsync(cancellationToken);
    }
}
