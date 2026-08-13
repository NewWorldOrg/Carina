namespace Carina.Domain.DriverStatus;

public sealed class DriverStatusSnapshot
{
    private DriverStatusSnapshot(DriverConnection connection, DateTimeOffset observedAt)
    {
        Connection = connection;
        ObservedAt = observedAt;
    }

    public DriverConnection Connection { get; }

    public DateTimeOffset ObservedAt { get; }

    public static DriverStatusSnapshot Observe(DriverConnection connection, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return new DriverStatusSnapshot(connection, timeProvider.GetUtcNow());
    }

    public static DriverStatusSnapshot Rehydrate(DriverConnection connection, DateTimeOffset observedAt)
        => new(connection, observedAt);
}
