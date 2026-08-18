using Carina.Contracts;
using Carina.Infrastructure.Events;

namespace Carina.Infrastructure.Tests;

public sealed class AppEventHubTests
{
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);

    private static async Task<IReadOnlyList<AppEventName>> Next(AppEventListener listener)
    {
        using var deadline = new CancellationTokenSource(Soon);

        return await listener.Take(deadline.Token);
    }

    [Fact]
    public async Task ASignalReachesEveryListener()
    {
        var hub = new AppEventHub();

        Assert.True(hub.TryListen(out AppEventListener? first));
        Assert.True(hub.TryListen(out AppEventListener? second));

        hub.Signal(AppEventName.Tuners);

        Assert.Equal([AppEventName.Tuners], await Next(first));
        Assert.Equal([AppEventName.Tuners], await Next(second));
    }

    [Fact]
    public async Task ASignalRaisedBeforeAnyoneReadsIsHeldRatherThanDropped()
    {
        var hub = new AppEventHub();

        Assert.True(hub.TryListen(out AppEventListener? listener));

        hub.Signal(AppEventName.Quality);

        Assert.Equal([AppEventName.Quality], await Next(listener));
    }

    [Fact]
    public async Task TheSameSignalRepeatedIsDeliveredOnce()
    {
        var hub = new AppEventHub();

        Assert.True(hub.TryListen(out AppEventListener? listener));

        for (int index = 0; index < 1000; index++)
        {
            hub.Signal(AppEventName.Tuners);
        }

        Assert.Equal([AppEventName.Tuners], await Next(listener));
    }

    [Fact]
    public async Task SignalsOfDifferentNamesAreAllDelivered()
    {
        var hub = new AppEventHub();

        Assert.True(hub.TryListen(out AppEventListener? listener));

        hub.Signal(AppEventName.Tuners);
        hub.Signal(AppEventName.Recordings);

        Assert.Equal([AppEventName.Tuners, AppEventName.Recordings], await Next(listener));
    }

    [Fact]
    public void EveryNameTheContractFixesIsAcceptable()
    {
        var hub = new AppEventHub();

        Assert.True(hub.TryListen(out _));

        foreach (AppEventName name in AppEventName.All)
        {
            hub.Signal(name);
        }
    }

    [Fact]
    public void AHubRefusesMoreListenersThanItsLimit()
    {
        var hub = new AppEventHub(listenerLimit: 1);

        Assert.True(hub.TryListen(out _));
        Assert.False(hub.TryListen(out AppEventListener? refused));
        Assert.Null(refused);
    }

    [Fact]
    public void ADisposedListenerLeavesRoomForTheNextOne()
    {
        var hub = new AppEventHub(listenerLimit: 1);

        Assert.True(hub.TryListen(out AppEventListener? first));
        first.Dispose();

        Assert.Equal(0, hub.ListenerCount);
        Assert.True(hub.TryListen(out _));
    }

    [Fact]
    public void AClosedHubTakesNoFurtherListeners()
    {
        var hub = new AppEventHub();
        hub.CloseAll();

        Assert.True(hub.IsClosed);
        Assert.False(hub.TryListen(out _));
    }

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

    [Fact]
    public async Task ClosingTheHubEndsTheWaitOfAListenerAlreadyInside()
    {
        var hub = new AppEventHub();

        Assert.True(hub.TryListen(out AppEventListener? listener));

        Task<IReadOnlyList<AppEventName>> waiting = Next(listener);
        hub.CloseAll();

        await Assert.ThrowsAnyAsync<Exception>(() => waiting);
    }
}
