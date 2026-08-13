using Carina.Api.Controllers.DriverStatus;
using Carina.Api.Responder;
using Carina.Api.Responder.DriverStatus;
using Carina.Api.Services;
using Carina.Contracts;
using Carina.Domain.DriverStatus;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Tests.Unit;

public sealed class GetDriverStatusActionTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 14, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RendersTheObservationThroughTheBaseResponder()
    {
        var observation = DriverObservation.Of(
            new DriverHello(DriverProtocol.Version, "instance-a", ["recording", "live"]),
            []);
        var action = new GetDriverStatusAction(new DriverStatusService(
            new StubDriverStatusReader(observation),
            new FixedTimeProvider(ObservedAt)));

        var response = await action.Invoke(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        var responder = Assert.IsType<BaseResponder<DriverStatusResponder>>(ok.Value);
        Assert.True(responder.Status);
        Assert.NotNull(responder.Data);
        Assert.Equal(DriverConnection.Connected, responder.Data.Connection);
        Assert.Equal("instance-a", responder.Data.Hello?.InstanceId);
        Assert.Equal(DriverProtocol.Version, responder.Data.AppProtocolVersion);
        Assert.False(responder.Data.DriverUpdateRequired);
        Assert.Equal(ObservedAt, responder.Data.ObservedAt);
    }

    [Fact]
    public async Task ABrokenReaderRendersAGenuine503()
    {
        var action = new GetDriverStatusAction(new DriverStatusService(
            new ThrowingDriverStatusReader(),
            new FixedTimeProvider(ObservedAt)));

        var response = await action.Invoke(CancellationToken.None);

        var faulted = Assert.IsType<ObjectResult>(response);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, faulted.StatusCode);
        var responder = Assert.IsType<BaseResponder<DriverStatusResponder>>(faulted.Value);
        Assert.False(responder.Status);
        Assert.Contains("The monitor is broken.", responder.Message, StringComparison.Ordinal);
        Assert.Null(responder.Data);
    }
}
