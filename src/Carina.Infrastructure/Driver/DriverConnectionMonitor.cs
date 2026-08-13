using Carina.Domain.DriverStatus;

namespace Carina.Infrastructure.Driver;

public sealed class DriverConnectionMonitor
{
    private volatile DriverObservation current = DriverObservation.NotConnected;

    public DriverObservation Current => current;

    public void Record(DriverObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        current = observation;
    }
}
