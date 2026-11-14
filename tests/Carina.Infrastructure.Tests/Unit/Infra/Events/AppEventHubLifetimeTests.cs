using Carina.Infrastructure.Events;

using Puppis.Contracts;

using static Carina.Infrastructure.Tests.Unit.Infra.Events.AppEventListening;

namespace Carina.Infrastructure.Tests.Unit.Infra.Events;

[Trait("Category", "Unit")]
public sealed class AppEventHubLifetimeTests
{
    [Fact]
    public async Task StoppingTheAppClosesTheHubSoNoListenerIsLeftHanging()
    {
        var hub = new AppEventHub();
        var lifetime = new AppEventHubLifetime(hub);

        Assert.True(hub.TryListen(out AppEventListener? listener));

        Task<IReadOnlyList<AppEventName>> waiting = Next(listener);
        await lifetime.StopAsync(CancellationToken.None);

        Assert.True(hub.IsClosed);
        await Assert.ThrowsAnyAsync<Exception>(() => waiting);
    }
}
