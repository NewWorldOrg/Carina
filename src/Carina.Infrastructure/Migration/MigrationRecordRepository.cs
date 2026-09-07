using Carina.Domain.Migration;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Migration;

public sealed class MigrationRecordRepository(CarinaDbContext context) : IMigrationRecordRepository
{
    public async Task SaveAsync(MigrationReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);

        context.Add(report.Run);
        context.AddRange(report.Tallies);
        context.AddRange(report.Details);
        context.AddRange(report.Omissions);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<MigrationRun?> LatestAsync(CancellationToken cancellationToken)
        => await context.Set<MigrationRun>()
            .AsNoTracking()
            .OrderByDescending(run => run.FinishedAt)
            .ThenByDescending(run => run.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<MigrationReport?> ReadAsync(MigrationRunId runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runId);

        MigrationRun? run = await context.Set<MigrationRun>()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == runId, cancellationToken);

        if (run is null)
        {
            return null;
        }

        List<MigrationTally> tallies = await context.Set<MigrationTally>()
            .AsNoTracking()
            .Where(tally => tally.RunId == runId)
            .OrderBy(tally => tally.Population)
            .ToListAsync(cancellationToken);

        List<MigrationDetail> details = await context.Set<MigrationDetail>()
            .AsNoTracking()
            .Where(detail => detail.RunId == runId)
            .OrderBy(detail => detail.Population)
            .ThenBy(detail => detail.Refusal)
            .ThenBy(detail => detail.Subject)
            .ToListAsync(cancellationToken);

        List<MigrationOmission> omissions = await context.Set<MigrationOmission>()
            .AsNoTracking()
            .Where(omission => omission.RunId == runId)
            .OrderBy(omission => omission.Subject)
            .ToListAsync(cancellationToken);

        return MigrationReport.Of(run, tallies, details, omissions);
    }
}
