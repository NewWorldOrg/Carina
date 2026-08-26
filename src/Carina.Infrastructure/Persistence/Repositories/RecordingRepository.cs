using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class RecordingRepository(CarinaDbContext context) : IRecordingRepository
{
    public async Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<Recording>()
            .FirstOrDefaultAsync(recording => recording.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Recording>> ListInFlightAsync(CancellationToken cancellationToken)
        => await context.Set<Recording>()
            .Where(recording => recording.Outcome == null)
            .OrderBy(recording => recording.ExpectedWindowEnd)
            .ThenBy(recording => recording.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Recording>> ListForReservationAsync(
        ReservationId reservationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservationId);

        return await context.Set<Recording>()
            .Where(recording => recording.ReservationId == reservationId)
            .OrderBy(recording => recording.StartedAtActual)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Recording recording, CancellationToken cancellationToken)
    {
        context.Add(recording);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(Recording recording, CancellationToken cancellationToken)
    {
        context.Update(recording);

        await context.SaveChangesAsync(cancellationToken);
    }
}
