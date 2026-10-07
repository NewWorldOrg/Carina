using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class LearningDataAmountReader(CarinaDbContext context) : ILearningDataAmountReader
{
    private static readonly string[] Holding =
    [
        nameof(LearningExtractionState.Done),
        nameof(LearningExtractionState.Partial),
    ];

    /// <summary>
    /// The tables of the segment entities, whose room on disk is the room the learning data takes.
    /// </summary>
    public static IReadOnlyList<string> MeasuredTables(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model.GetEntityTypes()
            .Where(entityType => AggregateRootOf(entityType).ClrType.Namespace == typeof(LearningExtraction).Namespace)
            .Select(entityType => entityType.GetTableName())
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<LearningDataAmount> ReadAsync(CancellationToken cancellationToken)
    {
        IQueryable<LearningExtraction> extractions = context.Set<LearningExtraction>().AsNoTracking();

        int recordings = await extractions.CountAsync(
            extraction => extraction.State == LearningExtractionState.Done
                || extraction.State == LearningExtractionState.Partial,
            cancellationToken);

        TimeSpan duration = await context.Database
            .SqlQuery<TimeSpan>(
                $"SELECT COALESCE(sum(read_through), interval '0') AS \"Value\" FROM segment_extraction WHERE state = ANY({Holding})")
            .SingleAsync(cancellationToken);

        string[] tables = [.. MeasuredTables(context.Model)];
        long bytes = await context.Database
            .SqlQuery<long>(
                $"SELECT COALESCE(sum(pg_total_relation_size(to_regclass(name))), 0)::bigint AS \"Value\" FROM unnest({tables}) AS name")
            .SingleAsync(cancellationToken);

        int waiting = await LearningBacklogReader.Awaiting(context).CountAsync(cancellationToken);

        return new LearningDataAmount(recordings, duration, bytes, waiting);
    }

    private static IEntityType AggregateRootOf(IEntityType entityType)
    {
        IEntityType current = entityType;

        while (current.FindOwnership() is { } ownership)
        {
            current = ownership.PrincipalEntityType;
        }

        return current;
    }
}
