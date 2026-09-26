using Carina.Domain.Reservations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Carina.Infrastructure.Persistence;

public static class ReservationWrites
{
    /// <summary>
    /// Saves the pending changes. When a reservation among them was changed in the store after it was
    /// read, detaches every tracked reservation and every pending change and throws
    /// <see cref="ReservationMovedMeanwhileException"/>.
    /// </summary>
    public static async Task SaveAsync(CarinaDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException moved) when (moved.Entries.Any(entry => entry.Entity is Reservation))
        {
            ReservationId[] stale =
            [
                .. moved.Entries.Select(entry => entry.Entity).OfType<Reservation>().Select(reservation => reservation.Id),
            ];

            foreach (EntityEntry held in context.ChangeTracker.Entries().ToList())
            {
                if (held.Entity is Reservation || held.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                {
                    held.State = EntityState.Detached;
                }
            }

            throw new ReservationMovedMeanwhileException(stale);
        }
    }
}
