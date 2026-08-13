namespace Carina.Domain.DriverStatus;

public interface IDriverStatusReader
{
    Task<DriverConnection> ReadAsync(CancellationToken cancellationToken);
}
