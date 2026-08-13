namespace Carina.Domain.DriverStatus;

public sealed class DriverStatusSnapshot
{
    private DriverStatusSnapshot(DriverObservation observation, DateTimeOffset observedAt)
    {
        Observation = observation;
        ObservedAt = observedAt;
    }

    public DriverObservation Observation { get; }

    public DateTimeOffset ObservedAt { get; }

    public static DriverStatusSnapshot Observe(DriverObservation observation, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new DriverStatusSnapshot(observation, timeProvider.GetUtcNow());
    }

    public static DriverStatusSnapshot Rehydrate(DriverObservation observation, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(observation);

        return new DriverStatusSnapshot(observation, observedAt);
    }
}
