using Carina.Contracts;
using Carina.Domain.DriverStatus;
using Carina.Infrastructure.Driver;

namespace Carina.Infrastructure.Tests;

public sealed class MonitoredDriverStatusReaderTests
{
    [Fact]
    public async Task HandsOutWhatTheMonitorHolds()
    {
        var monitor = new DriverConnectionMonitor();
        var reader = new MonitoredDriverStatusReader(monitor);

        Assert.Same(
            DriverObservation.NotConnected,
            await reader.ReadAsync(CancellationToken.None));

        var observation = DriverObservation.Of(
            new DriverHello(DriverProtocol.Version, "instance-a", ["recording"]),
            []);
        monitor.Record(observation);

        Assert.Same(observation, await reader.ReadAsync(CancellationToken.None));
    }
}
