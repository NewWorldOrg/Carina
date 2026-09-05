namespace Carina.Domain.Quality;

public interface IQualityIncidentRepository
{
    Task<QualityIncident?> FindAsync(QualityIncidentId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<QualityIncident>> ListUnsettledAsync(CancellationToken cancellationToken);

    Task AddAsync(QualityIncident incident, CancellationToken cancellationToken);

    Task SaveAsync(QualityIncident incident, CancellationToken cancellationToken);
}
