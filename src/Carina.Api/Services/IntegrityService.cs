using Carina.Api.Common;
using Carina.Domain.Base;
using Carina.Domain.Integrity;
using Carina.Infrastructure.Integrity;

namespace Carina.Api.Services;

public sealed record IntegrityFindings(IntegrityCheck? Check, PaginatedList<IntegrityFinding> Findings);

public sealed record IntegritySweep(IntegrityCheck? Swept, int Findings, SweepVerdict Verdict);

public sealed class IntegrityService(
    IntegrityCheckJob sweeps,
    IIntegrityCheckRepository checks,
    IntegritySettings settings,
    TimeProvider clock)
{
    public async Task<ServiceResult<IntegrityFindings>> ListAsync(
        IntegrityFindingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IntegrityCheck? latest = await checks.LatestAsync(cancellationToken);

        return ServiceResult<IntegrityFindings>.Success(new IntegrityFindings(
            latest,
            latest is null
                ? new PaginatedList<IntegrityFinding>([], 0, query.Page, query.PerPage)
                : await checks.ListFindingsAsync(latest.Id, query, cancellationToken)));
    }

    public async Task<ServiceResult<IntegritySweep>> RunAsync(CancellationToken cancellationToken)
    {
        IntegrityCheck? latest = await checks.LatestAsync(cancellationToken);
        SweepVerdict asked = SweepGuard.Of(
            sweeps.RunningCheck,
            latest?.FinishedAt,
            clock.GetUtcNow().UtcDateTime,
            settings.BetweenManualSweeps);

        if (!asked.IsAllowed)
        {
            return ServiceResult<IntegritySweep>.Success(new IntegritySweep(null, 0, asked));
        }

        IntegrityRun run = await sweeps.RunAsync(cancellationToken);

        return run.Swept is { } swept
            ? ServiceResult<IntegritySweep>.Success(
                new IntegritySweep(swept.Check, swept.Findings.Count, SweepVerdict.Allowed))
            : ServiceResult<IntegritySweep>.Success(new IntegritySweep(
                null,
                0,
                new SweepVerdict(SweepRefusal.OneIsAlreadyRunning, run.Running, null)));
    }
}
