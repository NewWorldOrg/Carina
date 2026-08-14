using Carina.Contracts;
using Carina.Infrastructure.Driver;

namespace Carina.Infrastructure.Tests;

public sealed class DriverReconnectCadenceTests
{
    private static DriverSupervisionSettings Settings => new(
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(1000),
        [DriverCapabilities.Recording, DriverCapabilities.Live],
        () => 1.0)
    {
        DrainPoll = TimeSpan.FromSeconds(15),
        MinimumFeedDwell = TimeSpan.FromSeconds(10),
    };

    [Fact]
    public void ASetbackClimbsInsteadOfRestartingAtTheFirstDelay()
    {
        var cadence = new DriverReconnectCadence(Settings);

        Assert.Equal(
            [100, 200, 400, 800, 1000, 1000],
            [.. Enumerable.Range(0, 6).Select(_ => cadence.AfterSetback().TotalMilliseconds)]);
    }

    [Fact]
    public void AFeedThatSurvivesTheDwellRestartsTheClimb()
    {
        var cadence = new DriverReconnectCadence(Settings);

        cadence.AfterSetback();
        cadence.AfterSetback();

        Assert.Equal(100, cadence.AfterFeed(TimeSpan.FromSeconds(10)).TotalMilliseconds);
        Assert.Equal(100, cadence.AfterFeed(TimeSpan.FromHours(5)).TotalMilliseconds);
    }

    [Fact]
    public void AFeedThatDoesNotSurviveTheDwellLeavesTheClimbWhereItWas()
    {
        var cadence = new DriverReconnectCadence(Settings);

        cadence.AfterSetback();
        cadence.AfterSetback();

        Assert.Equal(400, cadence.AfterFeed(TimeSpan.FromSeconds(9)).TotalMilliseconds);
        Assert.Equal(800, cadence.AfterFeed(TimeSpan.Zero).TotalMilliseconds);
    }

    [Fact]
    public void ADrainingDriverIsPolledOnAFlatCadenceOfItsOwn()
    {
        var cadence = new DriverReconnectCadence(Settings);

        Assert.Equal(
            [15000, 15000, 15000, 15000],
            [.. Enumerable.Range(0, 4).Select(_ => cadence.WhileDraining().TotalMilliseconds)]);
    }

    [Fact]
    public void DrainingNeitherClimbsNorRestartsTheReconnectDelay()
    {
        var cadence = new DriverReconnectCadence(Settings);

        cadence.AfterSetback();
        cadence.WhileDraining();
        cadence.WhileDraining();

        Assert.Equal(200, cadence.AfterSetback().TotalMilliseconds);
    }

    [Fact]
    public void RefusesASenselessWindow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DriverReconnectCadence(Settings with { DrainPoll = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DriverReconnectCadence(
                Settings with { MinimumFeedDwell = TimeSpan.FromSeconds(-1) }));
    }
}
