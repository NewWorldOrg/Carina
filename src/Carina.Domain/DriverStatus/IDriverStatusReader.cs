namespace Carina.Domain.DriverStatus;

public interface IDriverStatusReader
{
    Task<DriverObservation> ReadAsync(CancellationToken cancellationToken);
}
