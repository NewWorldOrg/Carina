using System.Linq.Expressions;

using Carina.Domain.Channels;
using Carina.Domain.Library;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class RecordingLibraryRepository(CarinaDbContext context) : IRecordingLibraryRepository
{
    public async Task<LibraryRecordingPage> SearchAsync(
        RecordingSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        IQueryable<Recording> found = ApplySort(ApplyCriteria(BuildBaseQuery(), criteria));
        IQueryable<Recording> asked = criteria.Quality is null ? found.Take(criteria.PerPage + 1) : found;

        List<LibraryRecordingSummary> rows = [];
        RecordingCursor? next = null;

        await foreach (Recording recording in asked.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            LibraryRecordingSummary row = LibraryRecordingSummary.Of(recording);

            if (criteria.Quality is { } level && row.Quality != level)
            {
                continue;
            }

            if (rows.Count == criteria.PerPage)
            {
                next = rows[^1].Cursor;

                break;
            }

            rows.Add(row);
        }

        return new LibraryRecordingPage(rows, next);
    }

    public async Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<Recording>()
            .AsNoTracking()
            .FirstOrDefaultAsync(recording => recording.Id == id, cancellationToken);
    }

    public async Task<int> DeleteAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<Recording>()
            .Where(recording => recording.Id == id && recording.Outcome != null)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private IQueryable<Recording> BuildBaseQuery()
        => context.Set<Recording>().AsNoTracking().Where(recording => recording.Outcome != null);

    private static IQueryable<Recording> ApplyCriteria(
        IQueryable<Recording> found,
        RecordingSearchCriteria criteria)
    {
        foreach (string word in criteria.Words)
        {
            string pattern = RecordingSearchPattern.Containing(word);

            found = found.Where(recording => EF.Functions.ILike(
                EF.Property<string>(recording, ProgrammeConfiguration.Searchable),
                pattern,
                RecordingSearchPattern.Escape));
        }

        if (criteria.Channels.Count > 0)
        {
            found = OnAnyOf(found, criteria.Channels);
        }

        if (criteria.Genre is { } genre)
        {
            found = found.Where(recording =>
                EF.Property<int[]>(recording, ProgrammeConfiguration.GenreKinds).Contains(genre));
        }

        if (criteria.Outcomes.Count > 0)
        {
            RecordingOutcome[] asked = [.. criteria.Outcomes];

            found = found.Where(recording => asked.Contains(recording.Outcome!.Value));
        }

        if (criteria.From is { } from)
        {
            found = found.Where(recording => recording.StartedAtActual >= from);
        }

        if (criteria.To is { } to)
        {
            found = found.Where(recording => recording.StartedAtActual < to);
        }

        if (criteria.After is { } after)
        {
            DateTime startedAt = after.StartedAt;
            Guid boundary = after.Id.Value;

            found = found.Where(recording => recording.StartedAtActual < startedAt
                || (recording.StartedAtActual == startedAt
                    && StoredOrder.Between(EF.Property<Guid>(recording, nameof(Recording.Id)), boundary) < 0));
        }

        return found;
    }

    private static IQueryable<Recording> ApplySort(IQueryable<Recording> found)
        => found.OrderByDescending(recording => recording.StartedAtActual).ThenByDescending(recording => recording.Id);

    private static IQueryable<Recording> OnAnyOf(
        IQueryable<Recording> found,
        IReadOnlyList<ProgrammeService> channels)
    {
        Expression<Func<Recording, bool>> nowhere = recording => false;

        return found.Where(channels.Aggregate(nowhere, (carried, channel) => Either(carried, On(channel))));
    }

    private static Expression<Func<Recording, bool>> On(ProgrammeService channel)
    {
        NetworkId network = new(channel.NetworkId);
        ServiceId carried = new(channel.ServiceId);

        return recording => recording.NetworkId == network && recording.ServiceId == carried;
    }

    private static Expression<Func<Recording, bool>> Either(
        Expression<Func<Recording, bool>> left,
        Expression<Func<Recording, bool>> right)
    {
        ParameterExpression recording = left.Parameters[0];
        Expression rejoined = new Rebound(right.Parameters[0], recording).Visit(right.Body);

        return Expression.Lambda<Func<Recording, bool>>(Expression.OrElse(left.Body, rejoined), recording);
    }

    private sealed class Rebound(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
