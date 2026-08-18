using Carina.Api.Common;
using Carina.Api.Services;
using Carina.Contracts;
using Carina.Domain.DriverStatus;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Api.Tests.Unit;

public sealed class DriverStatusServiceTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 14, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReportsTheObservationSeenByTheReader()
    {
        var observation = DriverObservation.Of(
            new DriverHello(DriverProtocol.Version, "instance-a", ["recording", "live"]),
            []);
        var service = new DriverStatusService(
            new StubDriverStatusReader(observation),
            new FixedTimeProvider(ObservedAt),
            NullLogger<DriverStatusService>.Instance);

        ServiceResult<DriverStatusSnapshot> result = await service.GetStatusAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Same(observation, result.Data.Observation);
        Assert.Equal(ObservedAt, result.Data.ObservedAt);
    }

    [Fact]
    public async Task ABrokenReaderBecomesAFailureNotAnException()
    {
        var service = new DriverStatusService(
            new ThrowingDriverStatusReader(),
            new FixedTimeProvider(ObservedAt),
            NullLogger<DriverStatusService>.Instance);

        ServiceResult<DriverStatusSnapshot> result = await service.GetStatusAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("The driver status is unavailable.", result.ErrorMessage);
        Assert.DoesNotContain("The monitor is broken.", result.ErrorMessage, StringComparison.Ordinal);
    }
}
