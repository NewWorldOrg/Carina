using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class LearningExtractionRepository(CarinaDbContext context) : ILearningExtractionRepository
{
    public async Task<LearningExtraction?> FindAsync(RecordingId recordingId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordingId);

        return await context.Set<LearningExtraction>()
            .AsNoTracking()
            .FirstOrDefaultAsync(extraction => extraction.RecordingId == recordingId, cancellationToken);
    }

    public async Task<IReadOnlyList<LearningExtraction>> ListAsync(
        LearningExtractionState state,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await context.Set<LearningExtraction>()
            .AsNoTracking()
            .Where(extraction => extraction.State == state)
            .OrderByDescending(extraction => extraction.Programme.RecordingStartedAt)
            .ThenBy(extraction => extraction.RecordingId)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(LearningExtraction extraction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extraction);

        context.Add(extraction);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            context.Entry(extraction).State = EntityState.Detached;
        }
    }

    public async Task<LearningExtractionWrite> SaveAsync(
        LearningExtraction extraction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extraction);

        context.Update(extraction);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsAnotherReading(exception))
        {
            return LearningExtractionWrite.AnotherIsReading;
        }
        finally
        {
            context.Entry(extraction).State = EntityState.Detached;
        }

        return LearningExtractionWrite.Written;
    }

    private static bool IsAnotherReading(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: LearningExtractionConfiguration.ReadingIndexName,
        };
}
