namespace Carina.Domain.Quality;

public interface IQualitySignalRollupRepository
{
    Task SaveAsync(IReadOnlyList<QualitySignalRollup> rollups, CancellationToken cancellationToken);

    Task<IReadOnlyList<QualitySignalRollup>> ListAsync(
        QualityWindow granularity,
        DateTime from,
        DateTime until,
        CancellationToken cancellationToken);

    Task<DateTime?> LatestWindowStartAsync(QualityWindow granularity, CancellationToken cancellationToken);
}
