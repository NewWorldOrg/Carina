using System.Linq.Expressions;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class RecordingDirectory(CarinaDbContext context) : IRecordingDirectory
{
    public async Task<PaginatedList<Recording>> ListAsync(
        RecordingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<Recording> found = context.Set<Recording>().AsNoTracking();

        if (query.Standing is { } standing)
        {
            found = standing is RecordingStanding.InFlight
                ? found.Where(recording => recording.Outcome == null)
                : found.Where(recording => recording.Outcome != null);
        }

        if (query.Outcomes.Count > 0)
        {
            found = EndedAnyOf(found, query.Outcomes);
        }

        if (query.Drops is { } drops)
        {
            found = drops switch
            {
                DropReading.Dropped => found.Where(recording =>
                    recording.CcMeasured && recording.CcDroppedPackets > 0),
                DropReading.Clean => found.Where(recording =>
                    recording.CcMeasured && recording.CcDroppedPackets == 0),
                _ => found.Where(recording => !recording.CcMeasured),
            };
        }

        if (query.Channels.Count > 0)
        {
            found = OnAnyOf(found, query.Channels);
        }

        if (query.From is { } from)
        {
            found = found.Where(recording => recording.StartedAtActual >= from);
        }

        if (query.To is { } to)
        {
            found = found.Where(recording => recording.StartedAtActual < to);
        }

        int total = await found.CountAsync(cancellationToken);
        IOrderedQueryable<Recording> ordered = (query.Sort, query.Descending) switch
        {
            (RecordingSort.ProgrammeStartsAt, false) => found.OrderBy(recording => recording.ProgrammeStartsAt),
            (RecordingSort.ProgrammeStartsAt, true) => found.OrderByDescending(recording => recording.ProgrammeStartsAt),
            (_, true) => found.OrderByDescending(recording => recording.StartedAtActual),
            _ => found.OrderBy(recording => recording.StartedAtActual),
        };

        List<Recording> page = await ordered
            .ThenBy(recording => recording.Id)
            .Skip((query.Page - 1) * query.PerPage)
            .Take(query.PerPage)
            .ToListAsync(cancellationToken);

        return new PaginatedList<Recording>(page, total, query.Page, query.PerPage);
    }

    public async Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<Recording>()
            .AsNoTracking()
            .FirstOrDefaultAsync(recording => recording.Id == id, cancellationToken);
    }

    public async Task<RecordingHalt> HaltAsync(
        RecordingId id,
        RecordingStopReason reason,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(reason);

        Recording? recording = await context.Set<Recording>()
            .FirstOrDefaultAsync(held => held.Id == id, cancellationToken);

        if (recording is null)
        {
            return RecordingHalt.NoSuchRecording;
        }

        if (!recording.IsInFlight)
        {
            return RecordingHalt.AlreadyEnded;
        }

        recording.Note(new OutcomeDetail(RecordingFault.StoppedByHand, null, reason.Value, at));
        recording.Abort(at);

        await context.SaveChangesAsync(cancellationToken);

        return RecordingHalt.Written;
    }

    public async Task<RecordingDiscard> DiscardAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        int discarded = await context.Set<Recording>()
            .Where(recording => recording.Id == id && recording.Outcome != null)
            .ExecuteDeleteAsync(cancellationToken);

        if (discarded > 0)
        {
            return RecordingDiscard.Discarded;
        }

        return await context.Set<Recording>().AnyAsync(recording => recording.Id == id, cancellationToken)
            ? RecordingDiscard.StillRecording
            : RecordingDiscard.NoSuchRecording;
    }

    private static IQueryable<Recording> EndedAnyOf(
        IQueryable<Recording> found,
        IReadOnlyList<RecordingOutcome> outcomes)
    {
        Expression<Func<Recording, bool>> nowhere = recording => false;

        return found.Where(outcomes.Aggregate(nowhere, (carried, outcome) => Either(carried, Ended(outcome))));
    }

    private static IQueryable<Recording> OnAnyOf(
        IQueryable<Recording> found,
        IReadOnlyList<ProgrammeService> services)
    {
        Expression<Func<Recording, bool>> nowhere = recording => false;

        return found.Where(services.Aggregate(nowhere, (carried, service) => Either(carried, On(service))));
    }

    private static Expression<Func<Recording, bool>> Ended(RecordingOutcome outcome)
        => recording => recording.Outcome == outcome;

    private static Expression<Func<Recording, bool>> On(ProgrammeService service)
    {
        var network = new NetworkId(service.NetworkId);
        var carried = new ServiceId(service.ServiceId);

        return recording => recording.NetworkId == network && recording.ServiceId == carried;
    }

    private static Expression<Func<Recording, bool>> Either(
        Expression<Func<Recording, bool>> left,
        Expression<Func<Recording, bool>> right)
    {
        ParameterExpression recording = left.Parameters[0];
        Expression rejoined = new Rebound(right.Parameters[0], recording).Visit(right.Body);

        return Expression.Lambda<Func<Recording, bool>>(
            Expression.OrElse(left.Body, rejoined),
            recording);
    }

    private sealed class Rebound(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
