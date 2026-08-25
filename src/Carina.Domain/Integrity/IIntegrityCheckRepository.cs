namespace Carina.Domain.Integrity;

public interface IIntegrityCheckRepository
{
    Task SaveAsync(IntegrityReport report, CancellationToken cancellationToken);

    Task<IntegrityCheck?> LatestAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<IntegrityFinding>> ListFindingsAsync(
        IntegrityCheckId checkId,
        CancellationToken cancellationToken);
}
