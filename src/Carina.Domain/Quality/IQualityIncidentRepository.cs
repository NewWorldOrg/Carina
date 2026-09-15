namespace Carina.Domain.Quality;

public interface IQualityIncidentRepository
{
    Task<IReadOnlyList<QualityIncident>> ListUnsettledAsync(CancellationToken cancellationToken);

    Task AddAsync(QualityIncident incident, CancellationToken cancellationToken);

    Task SaveAsync(QualityIncident incident, CancellationToken cancellationToken);
}
