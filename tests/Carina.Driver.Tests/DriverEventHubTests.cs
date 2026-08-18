using Carina.Contracts;
using Carina.Driver.Events;

namespace Carina.Driver.Tests;

public sealed class DriverEventHubTests
{
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);

    private static async Task<IReadOnlyList<string>> Next(DriverEventListener listener)
    {
        using var deadline = new CancellationTokenSource(Soon);

        return await listener.Take(deadline.Token);
    }

    [Fact]
    public async Task ASignalReachesEveryListener()
    {
        var hub = new DriverEventHub();

        Assert.True(hub.TryListen(out DriverEventListener? first));
        Assert.True(hub.TryListen(out DriverEventListener? second));

        hub.Signal(DriverEvents.Sessions);

        Assert.Equal([DriverEvents.Sessions], await Next(first));
        Assert.Equal([DriverEvents.Sessions], await Next(second));
    }

    [Theory]
    [InlineData("programs")]
    [InlineData("recordings")]
    [InlineData("sessionChanged")]
    [InlineData("")]
    public void ANameOutsideTheFixedSetIsRefused(string name)
    {
        var hub = new DriverEventHub();

        Assert.Throws<ArgumentException>(() => hub.Signal(name));
    }

    [Fact]
    public void EveryNameTheContractFixesIsAcceptable()
    {
        var hub = new DriverEventHub();

        Assert.True(hub.TryListen(out _));

        foreach (string name in DriverEvents.All)
        {
            hub.Signal(name);
        }
    }

    [Fact]
    public async Task TheSameSignalRepeatedIsDeliveredOnce()
    {
        var hub = new DriverEventHub();

        Assert.True(hub.TryListen(out DriverEventListener? listener));

        for (int index = 0; index < 1000; index++)
        {
            hub.Signal(DriverEvents.Tuners);
        }

        Assert.Equal([DriverEvents.Tuners], await Next(listener));
    }

    [Fact]
    public async Task SignalsOfDifferentNamesAreAllDelivered()
    {
        var hub = new DriverEventHub();

        Assert.True(hub.TryListen(out DriverEventListener? listener));

        hub.Signal(DriverEvents.Tuners);
        hub.Signal(DriverEvents.Sessions);

        var delivered = new List<string>();
        while (delivered.Count < 2)
        {
            delivered.AddRange(await Next(listener));
        }

        Assert.Contains(DriverEvents.Tuners, delivered);
        Assert.Contains(DriverEvents.Sessions, delivered);
    }

    [Fact]
    public async Task ASignalRaisedAfterOneIsTakenIsNotSwallowed()
    {
        var hub = new DriverEventHub();

        Assert.True(hub.TryListen(out DriverEventListener? listener));

        hub.Signal(DriverEvents.Sessions);
        Assert.Equal([DriverEvents.Sessions], await Next(listener));

        hub.Signal(DriverEvents.Sessions);
        Assert.Equal([DriverEvents.Sessions], await Next(listener));
    }

    [Fact]
    public void TheHubTakesOnlySoManyListeners()
    {
        var hub = new DriverEventHub(listenerLimit: 2);

        Assert.True(hub.TryListen(out _));
        Assert.True(hub.TryListen(out _));
        Assert.False(hub.TryListen(out DriverEventListener? refused));

        Assert.Null(refused);
        Assert.Equal(2, hub.ListenerCount);
    }

    [Fact]
    public void AListenerThatLeavesMakesRoomForTheNext()
    {
        var hub = new DriverEventHub(listenerLimit: 1);

        Assert.True(hub.TryListen(out DriverEventListener? listener));
        Assert.False(hub.TryListen(out _));

        listener.Dispose();

        Assert.True(hub.TryListen(out _));
    }

    [Fact]
    public async Task ClosingTheHubEndsEveryListener()
    {
        var hub = new DriverEventHub();

        Assert.True(hub.TryListen(out DriverEventListener? listener));

        hub.CloseAll();

        await Assert.ThrowsAsync<System.Threading.Channels.ChannelClosedException>(
            () => Next(listener)
        );
        Assert.Equal(0, hub.ListenerCount);
    }

    [Fact]
    public void SignallingWithNoListenerIsHarmless()
    {
        var hub = new DriverEventHub();

        hub.Signal(DriverEvents.Draining);

        Assert.Equal(0, hub.ListenerCount);
    }

    [Fact]
    public void AListenerArrivingAfterTheCloseIsRefused()
    {
        var hub = new DriverEventHub();

        hub.CloseAll();

        Assert.False(hub.TryListen(out _));
        Assert.Equal(0, hub.ListenerCount);
    }

    [Fact]
    public async Task ASignalSentJustBeforeTheCloseStillArrives()
    {
        var hub = new DriverEventHub();

        Assert.True(hub.TryListen(out DriverEventListener? listener));

        hub.Signal(DriverEvents.Draining);
        hub.CloseAll();

        Assert.Equal([DriverEvents.Draining], await Next(listener));
        await Assert.ThrowsAsync<System.Threading.Channels.ChannelClosedException>(
            () => Next(listener)
        );
    }
}
