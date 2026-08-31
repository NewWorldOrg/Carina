using Carina.Domain.Reservations;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class ReservationOutcomeRepository(CarinaDbContext context) : IReservationOutcomeRepository
{
    public async Task AddAsync(ReservationOutcome outcome, CancellationToken cancellationToken)
    {
        context.Add(outcome);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReservationOutcome>> ListAsync(
        OutcomeSpan span,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(span);

        IQueryable<ReservationOutcome> found = context.Set<ReservationOutcome>()
            .Where(outcome => outcome.OccurredAt >= span.From && outcome.OccurredAt <= span.To);

        if (span.Kind is { } kind)
        {
            found = found.Where(outcome => outcome.Kind == kind);
        }

        return await found
            .OrderByDescending(outcome => outcome.OccurredAt)
            .ThenBy(outcome => outcome.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReservationOutcome>> ListForReservationAsync(
        ReservationId reservationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservationId);

        return await context.Set<ReservationOutcome>()
            .Where(outcome => outcome.ReservationId == reservationId)
            .OrderByDescending(outcome => outcome.OccurredAt)
            .ThenBy(outcome => outcome.Id)
            .ToListAsync(cancellationToken);
    }
}
