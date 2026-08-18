using Carina.Domain.Scans;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class ScanRunRepository(CarinaDbContext context) : IScanRunRepository
{
    public async Task<ScanRunStart> StartAsync(ScanRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (!run.IsRunning)
        {
            throw new ArgumentException($"A scan starts as Running, but this one is {run.State}.", nameof(run));
        }

        context.Add(run);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsAnotherRunningScan(exception))
        {
            context.Entry(run).State = EntityState.Detached;

            ScanRun? running = await FindRunningAsync(cancellationToken);

            return ScanRunStart.RefusedBecauseOneIsRunning(running?.Id);
        }

        return ScanRunStart.Of(run);
    }

    public async Task<ScanRun?> FindAsync(ScanRunId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<ScanRun>().FirstOrDefaultAsync(run => run.Id == id, cancellationToken);
    }

    public async Task<ScanRun?> FindRunningAsync(CancellationToken cancellationToken)
        => await context.Set<ScanRun>()
            .FirstOrDefaultAsync(run => run.State == ScanRunState.Running, cancellationToken);

    public async Task<IReadOnlyList<ScanRun>> ListRecentAsync(int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await context.Set<ScanRun>()
            .OrderByDescending(run => run.StartedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(ScanRun run, CancellationToken cancellationToken)
    {
        context.Update(run);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAttemptAsync(ScanRunAttempt attempt, CancellationToken cancellationToken)
    {
        context.Add(attempt);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScanRunAttempt>> ListAttemptsAsync(
        ScanRunId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<ScanRunAttempt>()
            .Where(attempt => attempt.ScanRunId == id)
            .OrderBy(attempt => attempt.StartedAt)
            .ToListAsync(cancellationToken);
    }

    private static bool IsAnotherRunningScan(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ScanRunConfiguration.RunningIndexName,
        };
}
