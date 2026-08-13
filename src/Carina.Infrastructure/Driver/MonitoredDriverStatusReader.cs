using Carina.Domain.DriverStatus;

namespace Carina.Infrastructure.Driver;

public sealed class MonitoredDriverStatusReader(DriverConnectionMonitor monitor) : IDriverStatusReader
{
    public Task<DriverObservation> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(monitor.Current);
}
