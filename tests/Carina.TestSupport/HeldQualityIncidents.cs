using Carina.Domain.Quality;

namespace Carina.TestSupport;

public sealed class HeldQualityIncidents : IQualityIncidentRepository
{
    public List<QualityIncident> Incidents { get; } = [];

    public Task<IReadOnlyList<QualityIncident>> ListUnsettledAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<QualityIncident>>(
        [
            .. Incidents
                .Where(incident => incident.ResolvedAt is null)
                .OrderByDescending(incident => incident.DetectedAt),
        ]);

    public Task AddAsync(QualityIncident incident, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);

        Incidents.Add(incident);

        return Task.CompletedTask;
    }

    public Task SaveAsync(QualityIncident incident, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);

        if (!Incidents.Contains(incident))
        {
            Incidents.RemoveAll(held => held.Id.Equals(incident.Id));
            Incidents.Add(incident);
        }

        return Task.CompletedTask;
    }
}
