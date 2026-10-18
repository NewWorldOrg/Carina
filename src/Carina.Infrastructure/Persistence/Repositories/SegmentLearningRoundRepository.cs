using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class SegmentLearningRoundRepository(CarinaDbContext context) : ISegmentLearningRoundRepository
{
    public const long RoundLock = 5_243_197_610_105;

    private static readonly string TryTurnSql = $"SELECT pg_try_advisory_xact_lock({RoundLock}) AS \"Value\"";

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
        int? candidate = round.Candidate;

        return await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO segment_learning_round (day, taken_at, candidate) VALUES ({day}::date, {takenAt}::timestamptz, {candidate}::integer) ON CONFLICT (day) DO NOTHING",
            cancellationToken) is 1;
    }

    public async Task<DateTime?> LastLearnedAsync(CancellationToken cancellationToken)
        => await context.Set<SegmentLearningRound>()
            .AsNoTracking()
            .Where(round => round.Candidate != null)
            .MaxAsync(round => (DateTime?)round.TakenAt, cancellationToken);

    /// <summary>
    /// Opens a transaction on this context's connection and takes a lock that lasts as long as it does, without
    /// waiting for it; the context is then for holding the turn and nothing else.
    /// </summary>
    public async Task<IAsyncDisposable?> HoldAsync(CancellationToken cancellationToken)
    {
        IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            if (await context.Database.SqlQueryRaw<bool>(TryTurnSql).SingleAsync(cancellationToken))
            {
                return transaction;
            }
        }
        catch
        {
            await transaction.DisposeAsync();

            throw;
        }

        await transaction.DisposeAsync();

        return null;
    }
}
