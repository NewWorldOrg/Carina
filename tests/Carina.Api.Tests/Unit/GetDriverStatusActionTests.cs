using Carina.Api.Controllers.DriverStatus;
using Carina.Api.Responder;
using Carina.Api.Responder.DriverStatus;
using Carina.Api.Services;
using Carina.Domain.DriverStatus;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Tests.Unit;

public sealed class GetDriverStatusActionTests
{
    [Fact]
    public async Task RendersTheSnapshotThroughTheBaseResponder()
    {
        var observedAt = new DateTimeOffset(2026, 8, 13, 3, 0, 0, TimeSpan.Zero);
        var action = new GetDriverStatusAction(new DriverStatusService(
            new StubDriverStatusReader(DriverConnection.NotConnected),
            new FixedTimeProvider(observedAt)));

        var response = await action.Invoke(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        var responder = Assert.IsType<BaseResponder<DriverStatusResponder>>(ok.Value);
        Assert.True(responder.Status);
        Assert.NotNull(responder.Data);
        Assert.Equal(DriverConnection.NotConnected, responder.Data.Connection);
        Assert.Equal(observedAt, responder.Data.ObservedAt);
    }
}
