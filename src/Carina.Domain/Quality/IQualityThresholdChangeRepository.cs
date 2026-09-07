namespace Carina.Domain.Quality;

public interface IQualityThresholdChangeRepository
{
    Task AddAsync(QualityThresholdChange change, CancellationToken cancellationToken);

    Task<IReadOnlyList<QualityThresholdChange>> ListAsync(CancellationToken cancellationToken);
}
