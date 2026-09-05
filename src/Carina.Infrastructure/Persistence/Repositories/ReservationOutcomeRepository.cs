using System.Linq.Expressions;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
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

    public async Task<PaginatedList<ReservationOutcome>> ListAsync(
        ReservationOutcomeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<ReservationOutcome> found = context.Set<ReservationOutcome>().AsNoTracking();

        if (query.Kinds.Count > 0)
        {
            found = found.Where(AnyOf.Matching<ReservationOutcome, ReservationOutcomeKind>(query.Kinds, Classified));
        }

        if (query.Channels.Count > 0)
        {
            found = found.Where(AnyOf.Matching<ReservationOutcome, ProgrammeService>(query.Channels, On));
        }

        if (query.Rule is { } rule)
        {
            found = found.Where(outcome => outcome.RuleId == rule);
        }

        if (query.From is { } from)
        {
            found = found.Where(outcome => outcome.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            found = found.Where(outcome => outcome.OccurredAt < to);
        }

        int total = await found.CountAsync(cancellationToken);
        List<ReservationOutcome> page = await found
            .OrderByDescending(outcome => outcome.OccurredAt)
            .ThenBy(outcome => outcome.Id)
            .Skip((query.Page - 1) * query.PerPage)
            .Take(query.PerPage)
            .ToListAsync(cancellationToken);

        return new PaginatedList<ReservationOutcome>(page, total, query.Page, query.PerPage);
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

    private static Expression<Func<ReservationOutcome, bool>> Classified(ReservationOutcomeKind kind)
        => outcome => outcome.Kind == kind;

    private static Expression<Func<ReservationOutcome, bool>> On(ProgrammeService service)
    {
        var network = new NetworkId(service.NetworkId);
        var carried = new ServiceId(service.ServiceId);

        return outcome => outcome.NetworkId == network && outcome.ServiceId == carried;
    }
}
