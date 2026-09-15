using Carina.Domain.Base;

namespace Carina.Domain.Integrity;

public interface IIntegrityCheckRepository
{
    Task SaveAsync(IntegrityReport report, CancellationToken cancellationToken);

    Task<IntegrityCheck?> LatestAsync(CancellationToken cancellationToken);

    Task<PaginatedList<IntegrityFinding>> ListFindingsAsync(
        IntegrityCheckId checkId,
        IntegrityFindingQuery query,
        CancellationToken cancellationToken);

    Task<IntegrityFinding?> FindFindingAsync(
        IntegrityCheckId checkId,
        IntegrityFindingId findingId,
        CancellationToken cancellationToken);

    Task<bool> ThrowAwayFindingAsync(
        IntegrityCheckId checkId,
        IntegrityFindingId findingId,
        DateTime at,
        CancellationToken cancellationToken);
}
