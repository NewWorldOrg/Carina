using Carina.Contracts;

namespace Carina.Domain.Quality;

public interface IQualitySessionMeasurementRepository
{
    Task<QualitySessionMeasurement?> FindAsync(
        string driverInstanceId,
        SessionId session,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<QualitySessionMeasurement>> ListStartedBetweenAsync(
        DateTime from,
        DateTime until,
        CancellationToken cancellationToken);

    Task SaveAsync(QualitySessionMeasurement measurement, CancellationToken cancellationToken);
}
