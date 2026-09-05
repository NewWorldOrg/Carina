namespace Carina.Domain.Quality;

public interface IQualityThresholdRepository
{
    Task<IReadOnlyList<QualityThreshold>> ListAsync(CancellationToken cancellationToken);

    Task<QualityThreshold?> FindAsync(QualityThresholdKey key, CancellationToken cancellationToken);

    Task SaveAsync(QualityThreshold threshold, CancellationToken cancellationToken);
}
