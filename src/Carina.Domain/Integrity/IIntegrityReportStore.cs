namespace Carina.Domain.Integrity;

public interface IIntegrityReportStore
{
    Task SaveAsync(IntegritySweep sweep, CancellationToken cancellationToken);

    Task<IntegritySweep?> LatestAsync(CancellationToken cancellationToken);
}
