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
    private const string MarginAfterProperty = nameof(Reservation.MarginAfter);

    public async Task<PaginatedList<Reservation>> ListAsync(
        ReservationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<Reservation> found = context.Set<Reservation>().AsNoTracking();

        if (query.Standings.Count > 0)
        {
            found = found.Where(AnyOf.Matching<Reservation, ReservationStanding>(query.Standings, Standing));
        }

        if (query.Origin is { } origin)
        {
            found = origin is ReservationOrigin.ByRule
                ? found.Where(reservation => reservation.RuleId != null)
                : found.Where(reservation => reservation.RuleId == null);
        }

        if (query.Channels.Count > 0)
        {
            found = found.Where(AnyOf.Matching<Reservation, ProgrammeService>(query.Channels, On));
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

    public async Task<ReservationHealth> HealthAsync(DateTime at, CancellationToken cancellationToken)
    {
        DateTime moment = InUtc(at);

        var counted = await context.Set<Reservation>()
            .Where(reservation => reservation.RecordingOutcome == null)
            .Where(reservation => reservation.State == ReservationState.Scheduled
                                  || reservation.State == ReservationState.Conflict)
            .Where(reservation => reservation.EndAt.AddSeconds(
                EF.Property<int>(reservation, MarginAfterProperty)) > moment)
            .GroupBy(reservation => 1)
            .Select(ahead => new
            {
                Contended = ahead.Count(reservation => reservation.State == ReservationState.Conflict),
                ReceptionUnavailable = ahead.Count(reservation => reservation.ReceptionUnavailable),
                EpgDiverged = ahead.Count(reservation => reservation.EpgDiverged && reservation.AcknowledgedAt == null),
                EpgMissing = ahead.Count(reservation => reservation.EpgMissing && reservation.AcknowledgedAt == null),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return counted is null
            ? ReservationHealth.Clear(moment)
            : new ReservationHealth(
                moment,
                counted.Contended,
                counted.ReceptionUnavailable,
                counted.EpgDiverged,
                counted.EpgMissing);
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

    public async Task<IReadOnlyList<ReservationAwaitingOutcome>> ListAwaitingOutcomeAsync(
        DateTime through,
        CancellationToken cancellationToken)
    {
        DateTime moment = through.Kind is DateTimeKind.Utc
            ? through
            : throw new ArgumentException(
                $"A reservation ledger run is a UTC instant, but this one has Kind={through.Kind}.",
                nameof(through));

        return await context.Set<Reservation>()
            .Where(reservation => !context.Set<ReservationOutcome>()
                .Any(outcome => outcome.ReservationId == reservation.Id))
            .Where(reservation => reservation.RecordingOutcome == RecordingOutcome.Failed
                                  || (reservation.RecordingOutcome == null
                                      && (reservation.State == ReservationState.Scheduled
                                          || reservation.State == ReservationState.Conflict)
                                      && reservation.EndAt <= moment))
            .OrderBy(reservation => reservation.StartAt)
            .ThenBy(reservation => reservation.Id)
            .Select(reservation => new ReservationAwaitingOutcome(
                reservation,
                context.Set<Recording>().Any(recording => recording.ReservationId == reservation.Id)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Reservation>> ListClaimedOverAsync(
        ReservationWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        return await context.Set<Reservation>()
            .Where(reservation => reservation.StartedAt != null)
            .Where(reservation => reservation.EndAt >= window.From && reservation.StartAt <= window.To)
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

    public async Task<ReservationDiscard> DiscardAsync(
        ReservationId id,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        DateTime moment = InUtc(at);

        int discarded = await context.Set<Reservation>()
            .Where(reservation => reservation.Id == id)
            .Where(reservation => reservation.StartedAt == null || reservation.RecordingOutcome != null)
            .Where(reservation => !context.Set<Recording>().Any(recording => recording.ReservationId == id))
            .Where(reservation => (reservation.State != ReservationState.Scheduled
                                   && reservation.State != ReservationState.Conflict)
                                  || reservation.RecordingOutcome != null
                                  || reservation.EndAt.AddSeconds(
                                      EF.Property<int>(reservation, MarginAfterProperty)) <= moment)
            .ExecuteDeleteAsync(cancellationToken);

        if (discarded > 0)
        {
            return ReservationDiscard.Discarded;
        }

        if (!await context.Set<Reservation>().AnyAsync(reservation => reservation.Id == id, cancellationToken))
        {
            return ReservationDiscard.NoSuchReservation;
        }

        if (await context.Set<Recording>()
            .AnyAsync(recording => recording.ReservationId == id, cancellationToken))
        {
            return ReservationDiscard.RecordingCameOfIt;
        }

        return await context.Set<Reservation>().AnyAsync(
            reservation => reservation.Id == id
                           && reservation.StartedAt != null
                           && reservation.RecordingOutcome == null,
            cancellationToken)
            ? ReservationDiscard.TurningIntoARecording
            : ReservationDiscard.StillToBeRecorded;
    }

    private static DateTime InUtc(DateTime at)
        => at.Kind is DateTimeKind.Utc
            ? at
            : throw new ArgumentException(
                $"A moment is a UTC instant, but this one has Kind={at.Kind}.",
                nameof(at));

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
}
