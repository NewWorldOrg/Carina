using Carina.Contracts;
using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class TunerCapacityTests
{
    [Fact]
    public void ASatelliteTunerIsOneSeatThatEitherSatelliteSystemMayTake()
    {
        Assert.Equal(
            [TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110],
            BroadcastReception.Of(TunerKind.Satellite));
    }

    [Fact]
    public void ATerrestrialTunerServesTerrestrialAlone()
    {
        Assert.Equal([TuneSystem.IsdbT], BroadcastReception.Of(TunerKind.Terrestrial));
    }

    [Fact]
    public void ATunerThatNeverSaidWhatItReceivesServesNothing()
    {
        Assert.Empty(BroadcastReception.Of(TunerKind.Unspecified));
    }

    [Fact]
    public void EverySystemIsTheThreeThatCanBeTuned()
    {
        Assert.Equal(
            [TuneSystem.IsdbT, TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110],
            BroadcastReception.EverySystem);
    }

    [Fact]
    public void TwoSatelliteTunersAreTwoTunersReachingTwoSystems()
    {
        TunerCapacity capacity = Holding(Seat("adapter0", TunerKind.Satellite), Seat("adapter1", TunerKind.Satellite));

        Assert.Equal(2, capacity.SeatCount);
        Assert.Equal([TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110], capacity.Reachable.Order());
    }

    [Fact]
    public void TwoSatelliteTunersCannotTakeThreeSatelliteDemandsHoweverTheyAreSplit()
    {
        TunerCapacity capacity = Holding(Seat("adapter0", TunerKind.Satellite), Seat("adapter1", TunerKind.Satellite));

        Assert.True(capacity.CanSeat(Wanting((TuneSystem.IsdbSBs, 1), (TuneSystem.IsdbSCs110, 1))));
        Assert.False(capacity.CanSeat(Wanting((TuneSystem.IsdbSBs, 2), (TuneSystem.IsdbSCs110, 1))));
        Assert.False(capacity.CanSeat(Wanting((TuneSystem.IsdbSBs, 1), (TuneSystem.IsdbSCs110, 2))));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void TwoSatelliteTunersTakeAtMostTwoOfOneSatelliteSystem(int wanted, bool seated)
    {
        TunerCapacity capacity = Holding(Seat("adapter0", TunerKind.Satellite), Seat("adapter1", TunerKind.Satellite));

        Assert.Equal(seated, capacity.CanSeat(Wanting((TuneSystem.IsdbSBs, wanted))));
    }

    [Fact]
    public void OneOfEachKindCannotTakeBsAndCs110AtTheSameTime()
    {
        TunerCapacity capacity = Holding(Seat("adapter0", TunerKind.Terrestrial), Seat("adapter1", TunerKind.Satellite));

        Assert.True(capacity.CanSeat(Wanting((TuneSystem.IsdbT, 1), (TuneSystem.IsdbSBs, 1))));
        Assert.False(capacity.CanSeat(Wanting((TuneSystem.IsdbSBs, 1), (TuneSystem.IsdbSCs110, 1))));
        Assert.False(capacity.CanSeat(
            Wanting((TuneSystem.IsdbT, 1), (TuneSystem.IsdbSBs, 1), (TuneSystem.IsdbSCs110, 1))));
    }

    [Fact]
    public void ADemandOnASystemNoTunerServesCannotBeSeated()
    {
        TunerCapacity capacity = Holding(Seat("adapter0", TunerKind.Terrestrial));

        Assert.False(capacity.CanSeat(Wanting((TuneSystem.IsdbSBs, 1))));
        Assert.False(capacity.CanSeat(Wanting((TuneSystem.Unspecified, 1))));
    }

    [Fact]
    public void ADemandForNothingIsAlwaysSeated()
    {
        TunerCapacity capacity = Holding();

        Assert.True(capacity.CanSeat(Wanting()));
        Assert.True(capacity.CanSeat(Wanting((TuneSystem.IsdbT, 0))));
    }

    [Fact]
    public void ADemandForFewerThanNoTunersIsRefused()
    {
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => Holding(Seat("adapter0", TunerKind.Terrestrial)).CanSeat(Wanting((TuneSystem.IsdbT, -1))));

        Assert.Equal("demand", thrown.ParamName);
    }

    [Theory]
    [InlineData(TuneSystem.IsdbT, true)]
    [InlineData(TuneSystem.IsdbSBs, false)]
    [InlineData(TuneSystem.IsdbSCs110, false)]
    public void OnlyASystemSomeTunerServesCanBeServed(TuneSystem system, bool served)
    {
        Assert.Equal(served, Holding(Seat("adapter0", TunerKind.Terrestrial)).CanServe(system));
    }

    [Fact]
    public void AMachineWithNoTunersServesNoSystemAtAll()
    {
        TunerCapacity capacity = Holding();

        Assert.Equal(0, capacity.SeatCount);
        Assert.Empty(capacity.Reachable);
        Assert.False(capacity.CanServe(TuneSystem.IsdbT));
    }

    [Fact]
    public void AFaultedTunerStillCountsForWhatTheLedgerAsksFor()
    {
        TunerCapacity capacity = Holding(
            new TunerSeat("adapter0", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: true));

        Assert.True(capacity.CanServe(TuneSystem.IsdbT));
        Assert.True(capacity.CanSeat(Wanting((TuneSystem.IsdbT, 1))));
        Assert.Equal(1, capacity.SeatCount);
    }

    [Fact]
    public void AMachineWhoseEveryTunerIsFaultedStillReachesTheSystemsThoseTunersServe()
    {
        TunerCapacity capacity = Holding(
            new TunerSeat("adapter0", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: true),
            new TunerSeat("adapter1", BroadcastReception.Of(TunerKind.Satellite), Faulted: true));

        Assert.Equal(
            [TuneSystem.IsdbT, TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110],
            capacity.Reachable.Order());
        Assert.Empty(capacity.Healthy.Reachable);
    }

    [Fact]
    public void TheHealthyViewLeavesFaultedTunersOut()
    {
        TunerCapacity capacity = Holding(
            new TunerSeat("adapter0", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: true),
            Seat("adapter1", TunerKind.Satellite));

        Assert.Equal(1, capacity.Healthy.SeatCount);
        Assert.False(capacity.Healthy.CanServe(TuneSystem.IsdbT));
        Assert.True(capacity.Healthy.CanServe(TuneSystem.IsdbSBs));
        Assert.Equal(2, capacity.SeatCount);
    }

    [Fact]
    public void TheHealthyViewKeepsTheTunersNobodyCouldDescribe()
    {
        var capacity = new TunerCapacity([Seat("adapter0", TunerKind.Terrestrial)], ["adapter9"]);

        Assert.Equal(["adapter9"], capacity.Healthy.Undetermined);
    }

    [Fact]
    public void ACapacityBuiltFromNothingIsRefused()
    {
        Assert.Equal("seats", Assert.Throws<ArgumentNullException>(() => new TunerCapacity(null!, [])).ParamName);
        Assert.Equal("undetermined", Assert.Throws<ArgumentNullException>(() => new TunerCapacity([], null!)).ParamName);
    }

    [Fact]
    public void ADemandThatNamesNothingAtAllIsRefused()
    {
        Assert.Equal("demand", Assert.Throws<ArgumentNullException>(() => Holding().CanSeat(null!)).ParamName);
    }

    private static IReadOnlyDictionary<TuneSystem, int> Wanting(params (TuneSystem System, int Count)[] demand)
        => demand.ToDictionary(want => want.System, want => want.Count);

    private static TunerSeat Seat(string deviceId, TunerKind kind)
        => new(deviceId, BroadcastReception.Of(kind), Faulted: false);

    private static TunerCapacity Holding(params TunerSeat[] seats) => new(seats, []);
}
