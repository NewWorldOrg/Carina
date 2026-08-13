using Carina.Api.Services;
using Carina.Domain.DriverStatus;

namespace Carina.Api.Tests.Unit;

public sealed class DriverStatusServiceTests
{
    [Fact]
    public async Task ReportsTheConnectionSeenByTheReader()
    {
        var observedAt = new DateTimeOffset(2026, 8, 13, 3, 0, 0, TimeSpan.Zero);
        var service = new DriverStatusService(
            new StubDriverStatusReader(DriverConnection.NotConnected),
            new FixedTimeProvider(observedAt));

        var result = await service.GetStatusAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(DriverConnection.NotConnected, result.Data.Connection);
        Assert.Equal(observedAt, result.Data.ObservedAt);
    }
}
