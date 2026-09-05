using Carina.Domain.Encodings;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class EncodeJobRepository(CarinaDbContext context) : IEncodeJobRepository
{
    public async Task<EncodeJob?> FindAsync(EncodeJobId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<EncodeJob>().SingleOrDefaultAsync(job => job.Id == id, cancellationToken);
    }

    public async Task AddAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        context.Add(job);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        context.Update(job);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The oldest waiting job is moved to running by a conditional update, and only when that update
    /// changed one row is the job read back and handed over. The unique index over the running
    /// status is what refuses a second job while one runs, so a claim that hits it is answered as
    /// such rather than thrown; a row another claim took first changes nothing and says so.
    /// </summary>
    public async Task<EncodeClaim> ClaimNextAsync(DateTime at, CancellationToken cancellationToken)
    {
        if (at.Kind is not DateTimeKind.Utc)
        {
            throw new ArgumentException("A claim is timed in UTC.", nameof(at));
        }

        DateTime when = at;

        EncodeJobId? next = await context.Set<EncodeJob>()
            .AsNoTracking()
            .Where(row => row.Status == EncodeJobStatus.Queued)
            .OrderBy(row => row.QueuedAt)
            .ThenBy(row => row.Id)
            .Select(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (next is null)
        {
            return EncodeClaim.NothingWaiting();
        }

        int written;

        try
        {
            written = await context.Set<EncodeJob>()
                .Where(row => row.Id == next && row.Status == EncodeJobStatus.Queued)
                .ExecuteUpdateAsync(
                    update => update
                        .SetProperty(row => row.Status, EncodeJobStatus.Running)
                        .SetProperty(row => row.StartedAt, (DateTime?)when),
                    cancellationToken);
        }
        catch (DbUpdateException taken) when (IsAnotherRunning(taken))
        {
            return EncodeClaim.AnotherIsRunning();
        }
        catch (PostgresException taken) when (IsAnotherRunning(taken))
        {
            return EncodeClaim.AnotherIsRunning();
        }

        if (written is 0)
        {
            return EncodeClaim.TakenMeanwhile();
        }

        if (context.Set<EncodeJob>().Local.FindEntry(next) is { } stale)
        {
            stale.State = EntityState.Detached;
        }

        return EncodeClaim.Of(await context.Set<EncodeJob>().SingleAsync(row => row.Id == next, cancellationToken));
    }

    public async Task<IReadOnlyList<EncodeJob>> ListRunningAsync(CancellationToken cancellationToken)
        => await context.Set<EncodeJob>()
            .Where(row => row.Status == EncodeJobStatus.Running)
            .OrderBy(row => row.StartedAt)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The name goes into the row by a conditional update rather than through the change tracker,
    /// so that a refusal leaves the job in memory exactly as it was: the unique index over the root
    /// and the name is the one thing that decides who owns it, and a refusal is read off that index.
    /// </summary>
    public async Task<ArtefactClaim> ClaimArtefactAsync(
        EncodeJob job,
        EncodeFileName name,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(name);

        int written;

        try
        {
            written = await context.Set<EncodeJob>()
                .Where(row => row.Id == job.Id && row.Status == EncodeJobStatus.Running)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ArtefactName, name), cancellationToken);
        }
        catch (DbUpdateException taken) when (IsAnotherJobsArtefact(taken))
        {
            return ArtefactClaim.TakenByAnother;
        }
        catch (PostgresException taken) when (IsAnotherJobsArtefact(taken))
        {
            return ArtefactClaim.TakenByAnother;
        }

        if (written is 0)
        {
            throw new InvalidOperationException(
                "Only a job the ledger holds as running names its artefact, and the ledger holds no such row for this one.");
        }

        job.Name(name);

        return ArtefactClaim.Claimed;
    }

    private static bool IsAnotherRunning(DbUpdateException exception)
        => exception.InnerException is PostgresException inner && IsAnotherRunning(inner);

    private static bool IsAnotherRunning(PostgresException exception)
        => exception is
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: EncodeJobConfiguration.RunningIndexName,
        };

    private static bool IsAnotherJobsArtefact(DbUpdateException exception)
        => exception.InnerException is PostgresException inner && IsAnotherJobsArtefact(inner);

    private static bool IsAnotherJobsArtefact(PostgresException exception)
        => exception is
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: EncodeJobConfiguration.ArtefactIndexName,
        };
}
