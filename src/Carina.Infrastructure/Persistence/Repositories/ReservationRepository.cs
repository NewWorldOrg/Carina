using System.Linq.Expressions;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class ReservationRepository(CarinaDbContext context) : IReservationRepository
{
    public async Task<PaginatedList<Reservation>> ListAsync(
        ReservationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<Reservation> found = context.Set<Reservation>().AsNoTracking();

        if (query.Standings.Count > 0)
        {
            found = StandingAnyOf(found, query.Standings);
        }

        if (query.Origin is { } origin)
        {
            found = origin is ReservationOrigin.ByRule
                ? found.Where(reservation => reservation.RuleId != null)
                : found.Where(reservation => reservation.RuleId == null);
        }

        if (query.Channels.Count > 0)
        {
            found = OnAnyOf(found, query.Channels);
        }

        if (query.Keyword is { } keyword)
        {
            found = found.Where(reservation =>
                EF.Functions.ILike(reservation.SnapshotName, "%" + keyword + "%")
                || EF.Functions.ILike(reservation.SnapshotSummary, "%" + keyword + "%"));
        }

        if (query.From is { } from)
        {
            found = found.Where(reservation => reservation.StartAt >= from);
        }

        if (query.To is { } to)
        {
            found = found.Where(reservation => reservation.StartAt < to);
        }

        int total = await found.CountAsync(cancellationToken);
        IOrderedQueryable<Reservation> ordered = (query.Sort, query.Descending) switch
        {
            (ReservationSort.Priority, false) => found.OrderBy(reservation => reservation.Priority),
            (ReservationSort.Priority, true) => found.OrderByDescending(reservation => reservation.Priority),
            (_, true) => found.OrderByDescending(reservation => reservation.StartAt),
            _ => found.OrderBy(reservation => reservation.StartAt),
        };

        List<Reservation> page = await ordered
            .ThenBy(reservation => reservation.Id)
            .Skip((query.Page - 1) * query.PerPage)
            .Take(query.PerPage)
            .ToListAsync(cancellationToken);

        return new PaginatedList<Reservation>(page, total, query.Page, query.PerPage);
    }

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

    public async Task<ReservationDiscard> DiscardAsync(ReservationId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        int discarded = await context.Set<Reservation>()
            .Where(reservation => reservation.Id == id)
            .Where(reservation => reservation.StartedAt == null || reservation.RecordingOutcome != null)
            .Where(reservation => !context.Set<Recording>().Any(recording => recording.ReservationId == id))
            .ExecuteDeleteAsync(cancellationToken);

        if (discarded > 0)
        {
            return ReservationDiscard.Discarded;
        }

        if (!await context.Set<Reservation>().AnyAsync(reservation => reservation.Id == id, cancellationToken))
        {
            return ReservationDiscard.NoSuchReservation;
        }

        return await context.Set<Recording>()
            .AnyAsync(recording => recording.ReservationId == id, cancellationToken)
            ? ReservationDiscard.RecordingCameOfIt
            : ReservationDiscard.TurningIntoARecording;
    }

    private static IQueryable<Reservation> StandingAnyOf(
        IQueryable<Reservation> found,
        IReadOnlyList<ReservationStanding> standings)
    {
        Expression<Func<Reservation, bool>> nowhere = reservation => false;

        return found.Where(standings.Aggregate(nowhere, (carried, standing) => Either(carried, Standing(standing))));
    }

    private static IQueryable<Reservation> OnAnyOf(
        IQueryable<Reservation> found,
        IReadOnlyList<ProgrammeService> services)
    {
        Expression<Func<Reservation, bool>> nowhere = reservation => false;

        return found.Where(services.Aggregate(nowhere, (carried, service) => Either(carried, On(service))));
    }

    private static Expression<Func<Reservation, bool>> Standing(ReservationStanding standing)
    {
        string named = standing.ToString();

        return reservation =>
            EF.Property<string>(reservation, ReservationConfiguration.CompositeState) == named;
    }

    private static Expression<Func<Reservation, bool>> On(ProgrammeService service)
    {
        var network = new NetworkId(service.NetworkId);
        var carried = new ServiceId(service.ServiceId);

        return reservation => reservation.NetworkId == network && reservation.ServiceId == carried;
    }

    private static Expression<Func<Reservation, bool>> Either(
        Expression<Func<Reservation, bool>> left,
        Expression<Func<Reservation, bool>> right)
    {
        ParameterExpression reservation = left.Parameters[0];
        Expression rejoined = new Rebound(right.Parameters[0], reservation).Visit(right.Body);

        return Expression.Lambda<Func<Reservation, bool>>(
            Expression.OrElse(left.Body, rejoined),
            reservation);
    }

    private sealed class Rebound(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
