using Carina.Domain.Base;
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

    public async Task<PaginatedList<IntegrityFinding>> ListFindingsAsync(
        IntegrityCheckId checkId,
        IntegrityFindingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkId);
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<IntegrityFinding> found = context.Set<IntegrityFinding>()
            .AsNoTracking()
            .Where(finding => finding.CheckId == checkId && finding.ThrownAwayAt == null);

        int total = await found.CountAsync(cancellationToken);
        List<IntegrityFinding> page = await found
            .OrderBy(finding => finding.Root)
            .ThenBy(finding => finding.Path)
            .Skip((query.Page - 1) * query.PerPage)
            .Take(query.PerPage)
            .ToListAsync(cancellationToken);

        return new PaginatedList<IntegrityFinding>(page, total, query.Page, query.PerPage);
    }

    public async Task<IntegrityFinding?> FindFindingAsync(
        IntegrityCheckId checkId,
        IntegrityFindingId findingId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkId);
        ArgumentNullException.ThrowIfNull(findingId);

        return await context.Set<IntegrityFinding>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                finding => finding.CheckId == checkId && finding.Id == findingId,
                cancellationToken);
    }

    public async Task<bool> ThrowAwayFindingAsync(
        IntegrityCheckId checkId,
        IntegrityFindingId findingId,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkId);
        ArgumentNullException.ThrowIfNull(findingId);

        IntegrityFinding? finding = await context.Set<IntegrityFinding>()
            .FirstOrDefaultAsync(
                held => held.CheckId == checkId && held.Id == findingId,
                cancellationToken);

        if (finding is null || StrayFileDisposal.Refusal(finding) is not null)
        {
            return false;
        }

        finding.ThrowAway(at);

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
