namespace Carina.Domain.Quality;

public interface IQualityThresholdRepository
{
    Task<IReadOnlyList<QualityThreshold>> ListAsync(CancellationToken cancellationToken);

    Task<QualityThreshold?> FindAsync(QualityThresholdKey key, CancellationToken cancellationToken);

    Task SaveAsync(QualityThreshold threshold, CancellationToken cancellationToken);

    /// <summary>
    /// Waits until no other writer of the levels is underway, and keeps the others waiting until the transaction
    /// this is called in ends.
    /// </summary>
    Task TakeTurnAsync(CancellationToken cancellationToken);
}
