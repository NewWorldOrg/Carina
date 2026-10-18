using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class SegmentLearningRoundRepository(CarinaDbContext context) : ISegmentLearningRoundRepository
{
    public async Task<bool> TakenAsync(DateOnly day, CancellationToken cancellationToken)
        => await context.Set<SegmentLearningRound>().AsNoTracking().AnyAsync(round => round.Day == day, cancellationToken);

    /// <summary>
    /// Inserts the round unless one is kept for its day, in a single statement, so that of two takers of the same
    /// day only one is told it took it.
    /// </summary>
    public async Task<bool> TakeAsync(SegmentLearningRound round, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(round);

        DateOnly day = round.Day;
        DateTime takenAt = round.TakenAt;

        return await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO segment_learning_round (day, taken_at) VALUES ({day}::date, {takenAt}::timestamptz) ON CONFLICT (day) DO NOTHING",
            cancellationToken) is 1;
    }
}
