using Carina.Contracts;
using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class TunerCapacityTests
{
    [Fact]
    public void ASatelliteTunerIsOneSeatThatEitherSatelliteSystemMayTake()
    {
        IReadOnlyList<TuneSystem> serves = BroadcastReception.Of(TunerKind.Satellite);

        Assert.Equal([TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110], serves);
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
    public void TwoSatelliteTunersAreTwoSeatsAcrossTwoSystemsRatherThanFourTuners()
    {
        TunerCapacity capacity = Holding(
            Seat("adapter0", TunerKind.Satellite),
            Seat("adapter1", TunerKind.Satellite));

        Assert.Equal(2, capacity.Seats.Count);
        Assert.Equal([TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110], capacity.Served);
        Assert.All(capacity.Seats, seat => Assert.Equal(2, seat.Serves.Count));
    }

    [Fact]
    public void AMachineWithBothKindsServesAllThreeSystems()
    {
        TunerCapacity capacity = Holding(
            Seat("adapter0", TunerKind.Terrestrial),
            Seat("adapter1", TunerKind.Satellite));

        Assert.Equal(
            [TuneSystem.IsdbT, TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110],
            capacity.Served);
        Assert.Equal(2, capacity.Seats.Count);
    }

    [Theory]
    [InlineData(TuneSystem.IsdbT, true)]
    [InlineData(TuneSystem.IsdbSBs, false)]
    [InlineData(TuneSystem.IsdbSCs110, false)]
    public void OnlyASystemSomeSeatServesCanBeServed(TuneSystem system, bool served)
    {
        Assert.Equal(served, Holding(Seat("adapter0", TunerKind.Terrestrial)).CanServe(system));
    }

    [Fact]
    public void AMachineWithNoSeatsServesNoSystemAtAll()
    {
        TunerCapacity capacity = Holding();

        Assert.Empty(capacity.Seats);
        Assert.Empty(capacity.Served);
        Assert.False(capacity.CanServe(TuneSystem.IsdbT));
    }

    [Fact]
    public void AFaultedSeatIsStillASeatTheLedgerAsksFor()
    {
        TunerCapacity capacity = Holding(
            new TunerSeat("adapter0", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: true));

        Assert.True(capacity.CanServe(TuneSystem.IsdbT));
        Assert.True(Assert.Single(capacity.Seats).Faulted);
    }

    private static TunerSeat Seat(string deviceId, TunerKind kind)
        => new(deviceId, BroadcastReception.Of(kind), Faulted: false);

    private static TunerCapacity Holding(params TunerSeat[] seats) => new(seats, []);
}
