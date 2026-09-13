using Carina.Domain.Base;
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
        context.AddRange(report.Losses);
        context.AddRange(report.Standings);
        context.AddRange(report.ChannelProposals);
        context.AddRange(report.RuleProposals);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<MigrationRun?> LatestAsync(CancellationToken cancellationToken)
        => await context.Set<MigrationRun>()
            .AsNoTracking()
            .OrderByDescending(run => run.FinishedAt)
            .ThenByDescending(run => run.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<MigrationRecordSummary?> SummariseAsync(CancellationToken cancellationToken)
    {
        MigrationRun? latest = await LatestAsync(cancellationToken);

        if (latest is null)
        {
            return null;
        }

        List<MigrationTally> tallies = await context.Set<MigrationTally>()
            .AsNoTracking()
            .Where(tally => tally.RunId == latest.Id)
            .ToListAsync(cancellationToken);

        List<MigrationLoss> losses = await context.Set<MigrationLoss>()
            .AsNoTracking()
            .Where(loss => loss.RunId == latest.Id)
            .ToListAsync(cancellationToken);

        Dictionary<MigrationRefusal, int> counted = await context.Set<MigrationDetail>()
            .AsNoTracking()
            .Where(detail => detail.RunId == latest.Id)
            .GroupBy(detail => detail.Refusal)
            .Select(group => new { Refusal = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Refusal, row => row.Count, cancellationToken);

        IQueryable<MigrationRun> rehearsals = context.Set<MigrationRun>()
            .AsNoTracking()
            .Where(run => run.Pass == MigrationPass.Rehearsal);

        return new MigrationRecordSummary(
            latest,
            [.. tallies.OrderBy(tally => tally.Population)],
            MigrationRefusalCount.EveryOne(counted),
            [.. losses.OrderBy(loss => loss.Subject)],
            await rehearsals.CountAsync(cancellationToken),
            await rehearsals.MaxAsync(run => (DateTime?)run.FinishedAt, cancellationToken));
    }

    public async Task<PaginatedList<MigrationDetail>> ListDetailsAsync(
        MigrationRunId runId,
        MigrationDetailQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runId);
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<MigrationDetail> found = context.Set<MigrationDetail>()
            .AsNoTracking()
            .Where(detail => detail.RunId == runId);

        int total = await found.CountAsync(cancellationToken);

        List<MigrationDetail> rows = await found
            .OrderBy(detail => detail.Refusal)
            .ThenBy(detail => detail.Population)
            .ThenBy(detail => detail.Subject)
            .Skip((query.Page - 1) * query.PerPage)
            .Take(query.PerPage)
            .ToListAsync(cancellationToken);

        return new PaginatedList<MigrationDetail>(rows, total, query.Page, query.PerPage);
    }

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

        List<MigrationLoss> losses = await context.Set<MigrationLoss>()
            .AsNoTracking()
            .Where(loss => loss.RunId == runId)
            .OrderBy(loss => loss.Subject)
            .ToListAsync(cancellationToken);

        List<MigrationStanding> standings = await context.Set<MigrationStanding>()
            .AsNoTracking()
            .Where(standing => standing.RunId == runId)
            .OrderBy(standing => standing.Subject)
            .ToListAsync(cancellationToken);

        List<MigrationChannelProposal> channelProposals = await context.Set<MigrationChannelProposal>()
            .AsNoTracking()
            .Where(proposal => proposal.RunId == runId)
            .OrderBy(proposal => proposal.NetworkId)
            .ThenBy(proposal => proposal.ServiceId)
            .ToListAsync(cancellationToken);

        List<MigrationRuleProposal> ruleProposals = await context.Set<MigrationRuleProposal>()
            .AsNoTracking()
            .Where(proposal => proposal.RunId == runId)
            .OrderBy(proposal => proposal.SourceRow)
            .ToListAsync(cancellationToken);

        return MigrationReport.Of(run, tallies, details, losses, standings, channelProposals, ruleProposals);
    }
}
