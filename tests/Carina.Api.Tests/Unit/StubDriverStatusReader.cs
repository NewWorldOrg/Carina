using Carina.Domain.DriverStatus;

namespace Carina.Api.Tests.Unit;

internal sealed class StubDriverStatusReader(DriverConnection connection) : IDriverStatusReader
{
    public Task<DriverConnection> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(connection);
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
