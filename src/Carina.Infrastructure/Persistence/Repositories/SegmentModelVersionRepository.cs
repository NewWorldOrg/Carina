using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class SegmentModelVersionRepository(CarinaDbContext context) : ISegmentModelVersionRepository
{
    public const long WriterLock = 5_243_197_610_103;

    private static readonly string TakeTurnSql = $"SELECT pg_advisory_xact_lock({WriterLock})";

    public async Task<IReadOnlyList<SegmentModelVersion>> ListAsync(CancellationToken cancellationToken)
        => await context.Set<SegmentModelVersion>()
            .AsNoTracking()
            .OrderByDescending(version => version.Number)
            .ToListAsync(cancellationToken);

    public async Task<SegmentModelVersion?> ChangeAsync(SegmentVersionChange change, DateTime at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(TakeTurnSql, cancellationToken);

        if (SegmentModelVersion.Change(await ListAsync(cancellationToken), change, at) is not { } changed)
        {
            return null;
        }

        await RestateAsync(changed.Written, cancellationToken);
        await transaction.CommitAsync(CancellationToken.None);

        return changed.Active;
    }

    public async Task<SegmentBuiltInKept?> KeepBuiltInAsync(SegmentModel builtIn, DateTime at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(builtIn);

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(TakeTurnSql, cancellationToken);

        if (SegmentModelVersion.KeepBuiltIn(await ListAsync(cancellationToken), builtIn, at) is not { } kept)
        {
            return null;
        }

        await RestateAsync(kept.Moved, cancellationToken);
        context.Add(kept.Added);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(CancellationToken.None);
        context.Entry(kept.Added).State = EntityState.Detached;

        return kept;
    }

    public async Task<SegmentModelVersion> AddCandidateAsync(SegmentModel model, SegmentEvaluation evaluation, DateTime trainedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(evaluation);

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(TakeTurnSql, cancellationToken);

        int highest = await context.Set<SegmentModelVersion>().MaxAsync(version => (int?)version.Number, cancellationToken) ?? 0;
        SegmentModelVersion candidate = SegmentModelVersion.Learned(highest + 1, model, evaluation, trainedAt);

        context.Add(candidate);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(CancellationToken.None);
        context.Entry(candidate).State = EntityState.Detached;

        return candidate;
    }

    public async Task<SegmentModelVersion> ActiveAsync(CancellationToken cancellationToken)
        => await context.Set<SegmentModelVersion>()
               .AsNoTracking()
               .SingleOrDefaultAsync(version => version.State == SegmentModelVersionState.Active, cancellationToken)
           ?? throw new InvalidOperationException("No version of the model is in use; the built-in version is kept and put in use when the application starts.");

    public async Task<SegmentModelVersion?> FindAsync(int number, CancellationToken cancellationToken)
        => await context.Set<SegmentModelVersion>()
            .AsNoTracking()
            .SingleOrDefaultAsync(version => version.Number == number, cancellationToken);

    private async Task RestateAsync(IReadOnlyList<SegmentModelVersion> written, CancellationToken cancellationToken)
    {
        foreach (SegmentModelVersion version in written)
        {
            await context.Set<SegmentModelVersion>()
                .Where(row => row.Number == version.Number)
                .ExecuteUpdateAsync(
                    row => row
                        .SetProperty(held => held.State, version.State)
                        .SetProperty(held => held.ByHand, version.ByHand)
                        .SetProperty(held => held.AdoptedAt, version.AdoptedAt)
                        .SetProperty(held => held.RetiredAt, version.RetiredAt)
                        .SetProperty(held => held.ChosenByHandAt, version.ChosenByHandAt)
                        .SetProperty(held => held.RolledBackAt, version.RolledBackAt)
                        .SetProperty(held => held.Rollback, version.Rollback),
                    cancellationToken);
        }
    }
}
