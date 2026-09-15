using Carina.Domain.Quality;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class QualityIncidentRepository(CarinaDbContext context) : IQualityIncidentRepository
{
    public async Task<IReadOnlyList<QualityIncident>> ListUnsettledAsync(CancellationToken cancellationToken)
        => await context.Set<QualityIncident>()
            .Where(incident => incident.ResolvedAt == null)
            .OrderByDescending(incident => incident.DetectedAt)
            .ThenBy(incident => incident.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(QualityIncident incident, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);

        context.Add(incident);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(QualityIncident incident, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);

        context.Update(incident);

        await context.SaveChangesAsync(cancellationToken);
    }
}
