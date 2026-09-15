using Carina.Contracts;
using Carina.Domain.Quality;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class QualitySessionMeasurementRepository(CarinaDbContext context) : IQualitySessionMeasurementRepository
{
    public async Task<QualitySessionMeasurement?> FindAsync(
        string driverInstanceId,
        SessionId session,
        CancellationToken cancellationToken)
        => await context.Set<QualitySessionMeasurement>().FindAsync([driverInstanceId, session], cancellationToken);

    public async Task<IReadOnlyList<QualitySessionMeasurement>> ListOpenAsync(CancellationToken cancellationToken)
        => await context.Set<QualitySessionMeasurement>()
            .Where(measurement => measurement.EndedAt == null)
            .OrderBy(measurement => measurement.StartedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QualitySessionMeasurement>> ListStartedBetweenAsync(
        DateTime from,
        DateTime until,
        CancellationToken cancellationToken)
        => await context.Set<QualitySessionMeasurement>()
            .AsNoTracking()
            .Where(measurement => measurement.StartedAt >= from && measurement.StartedAt < until)
            .OrderBy(measurement => measurement.StartedAt)
            .ToListAsync(cancellationToken);

    public async Task SaveAsync(QualitySessionMeasurement measurement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(measurement);

        if (context.Entry(measurement).State is EntityState.Detached)
        {
            QualitySessionMeasurement? held = await context.Set<QualitySessionMeasurement>()
                .FindAsync([measurement.DriverInstanceId, measurement.Session], cancellationToken);

            if (held is null)
            {
                context.Add(measurement);
            }
            else
            {
                context.Entry(held).CurrentValues.SetValues(measurement);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
