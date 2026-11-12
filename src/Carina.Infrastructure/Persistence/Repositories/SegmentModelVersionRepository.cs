using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class SegmentModelVersionRepository(CarinaDbContext context) : ISegmentModelVersionRepository
{
    public const long WriterLock = 5_243_197_610_103;

    /// <summary>
    /// The highest number given, 0 while none has been: of the versions kept, and of what the rounds of learning, the
    /// detections, the viewers' skips and the chapters of an encode remember, which outlasts a version let go or a
    /// table emptied.
    /// </summary>
    private const string HighestGivenSql =
        """
        SELECT coalesce(greatest(
            (SELECT max(number) FROM segment_model_version),
            (SELECT max(candidate) FROM segment_learning_round),
            (SELECT max(in_use_version) FROM segment_learning_round),
            (SELECT max(model_version) FROM segment_detection),
            (SELECT max(model_version) FROM segment_skip_event),
            (SELECT max(chapters_model_version) FROM encode_job)), 0) AS "Value"
        """;

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

        if (SegmentModelVersion.KeepBuiltIn(await ListAsync(cancellationToken), builtIn, at, await HighestGivenAsync(cancellationToken)) is not { } kept)
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

        int number = SegmentModelVersion.NumberAfter(await HighestGivenAsync(cancellationToken));
        SegmentModelVersion candidate = SegmentModelVersion.Learned(number, model, evaluation, trainedAt);

        context.Add(candidate);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(CancellationToken.None);
        context.Entry(candidate).State = EntityState.Detached;

        return candidate;
    }

    public async Task<IReadOnlySet<int>> NamedAmongAsync(IReadOnlyCollection<int> numbers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(numbers);

        if (numbers.Count is 0)
        {
            return new HashSet<int>();
        }

        int[] asked = [.. numbers];
        List<int> named = await context.Database
            .SqlQuery<int>(
                $"""
                SELECT asked.number AS "Value"
                FROM unnest({asked}) AS asked(number)
                WHERE EXISTS (SELECT 1 FROM segment_detection WHERE model_version = asked.number)
                   OR EXISTS (SELECT 1 FROM segment_skip_event WHERE model_version = asked.number)
                   OR EXISTS (SELECT 1 FROM encode_job WHERE chapters_model_version = asked.number)
                   OR EXISTS (SELECT 1 FROM segment_learning_round WHERE in_use_version = asked.number)
                """)
            .ToListAsync(cancellationToken);

        return named.ToHashSet();
    }

    /// <summary>
    /// Deletes, in one statement under the writers' lock, the versions named that are still candidates learning made
    /// that nobody put in use or chose and that nothing names, the statement holding again what
    /// <see cref="SegmentModelVersion.IsLetGoBefore"/> says of them for a change made since the domain read them.
    /// </summary>
    public async Task<IReadOnlyList<int>> LetGoAsync(IReadOnlyCollection<int> numbers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(numbers);

        if (numbers.Count is 0)
        {
            return [];
        }

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(TakeTurnSql, cancellationToken);

        int[] named = [.. numbers];
        string candidate = nameof(SegmentModelVersionState.Candidate);
        List<int> letGo = await context.Database
            .SqlQuery<int>(
                $"""
                DELETE FROM segment_model_version AS version
                WHERE version.number = ANY({named})
                  AND version.state = {candidate}
                  AND NOT version.built_in
                  AND version.adopted_at IS NULL
                  AND version.chosen_by_hand_at IS NULL
                  AND NOT EXISTS (SELECT 1 FROM segment_detection WHERE model_version = version.number)
                  AND NOT EXISTS (SELECT 1 FROM segment_skip_event WHERE model_version = version.number)
                  AND NOT EXISTS (SELECT 1 FROM encode_job WHERE chapters_model_version = version.number)
                  AND NOT EXISTS (SELECT 1 FROM segment_learning_round WHERE in_use_version = version.number)
                RETURNING version.number AS "Value"
                """)
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(CancellationToken.None);

        return [.. letGo.Order()];
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

    private async Task<int> HighestGivenAsync(CancellationToken cancellationToken)
        => await context.Database.SqlQueryRaw<int>(HighestGivenSql).SingleAsync(cancellationToken);

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
