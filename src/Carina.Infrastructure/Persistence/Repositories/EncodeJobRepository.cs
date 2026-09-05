using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
        ArgumentNullException.ThrowIfNull(job);

        context.Update(job);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.Entry(job).State = EntityState.Detached;

            throw new EncodeJobMovedMeanwhileException(job.Id);
        }
    }

    public async Task<PaginatedList<EncodeJob>> ListAsync(EncodeJobQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<EncodeJob> asked = context.Set<EncodeJob>().AsNoTracking();

        if (query.Statuses.Count > 0)
        {
            asked = asked.Where(row => query.Statuses.Contains(row.Status));
        }

        int total = await asked.CountAsync(cancellationToken);
        List<EncodeJob> page = await asked
            .OrderByDescending(row => row.QueuedAt)
            .ThenByDescending(row => row.Id)
            .Skip((query.Page - 1) * query.PerPage)
            .Take(query.PerPage)
            .ToListAsync(cancellationToken);

        return new PaginatedList<EncodeJob>(page, total, query.Page, query.PerPage);
    }

    public async Task<IReadOnlyList<EncodeJob>> ListForRecordingAsync(RecordingId recordingId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordingId);

        return await context.Set<EncodeJob>()
            .Where(row => row.RecordingId == recordingId)
            .OrderBy(row => row.QueuedAt)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
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
        await CatchUpWithTheRowAsync(job, cancellationToken);

        return ArtefactClaim.Claimed;
    }

    /// <summary>
    /// A conditional update moves the row's version on without the tracker seeing it, so a tracked
    /// job's version is read again afterwards; otherwise the next save would take the job's own
    /// update for another hand's.
    /// </summary>
    private async Task CatchUpWithTheRowAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        EntityEntry<EncodeJob> entry = context.Entry(job);

        if (entry.State is EntityState.Detached)
        {
            return;
        }

        uint version = await context.Set<EncodeJob>()
            .AsNoTracking()
            .Where(row => row.Id == job.Id)
            .Select(row => EF.Property<uint>(row, EncodeJobConfiguration.ConcurrencyToken))
            .SingleAsync(cancellationToken);

        PropertyEntry<EncodeJob, uint> token = entry.Property<uint>(EncodeJobConfiguration.ConcurrencyToken);
        token.OriginalValue = version;
        token.CurrentValue = version;
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
