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

    private static bool IsAnotherJobsArtefact(DbUpdateException exception)
        => exception.InnerException is PostgresException inner && IsAnotherJobsArtefact(inner);

    private static bool IsAnotherJobsArtefact(PostgresException exception)
        => exception is
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: EncodeJobConfiguration.ArtefactIndexName,
        };
}
