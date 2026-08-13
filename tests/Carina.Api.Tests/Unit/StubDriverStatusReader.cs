using Carina.Domain.DriverStatus;

namespace Carina.Api.Tests.Unit;

internal sealed class StubDriverStatusReader(DriverObservation observation) : IDriverStatusReader
{
    public Task<DriverObservation> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(observation);
}

internal sealed class ThrowingDriverStatusReader : IDriverStatusReader
{
    public Task<DriverObservation> ReadAsync(CancellationToken cancellationToken)
        => throw new InvalidOperationException("The monitor is broken.");
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
