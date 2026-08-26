using Carina.Domain.Integrity;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Integrity;

public sealed class IntegrityCheckRepository(CarinaDbContext context) : IIntegrityCheckRepository
{
    public async Task SaveAsync(IntegrityReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);

        context.Add(report.Check);
        context.AddRange(report.Findings);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IntegrityCheck?> LatestAsync(CancellationToken cancellationToken)
        => await context.Set<IntegrityCheck>()
            .AsNoTracking()
            .OrderByDescending(check => check.FinishedAt)
            .ThenByDescending(check => check.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<IntegrityFinding>> ListFindingsAsync(
        IntegrityCheckId checkId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkId);

        return await context.Set<IntegrityFinding>()
            .AsNoTracking()
            .Where(finding => finding.CheckId == checkId)
            .OrderBy(finding => finding.Root)
            .ThenBy(finding => finding.Path)
            .ToListAsync(cancellationToken);
    }
}
