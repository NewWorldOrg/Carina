using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Infrastructure.Channels;

namespace Carina.Infrastructure.Tests.Channels;

public sealed class TunerCapacityDirectoryTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ASatelliteTunerIsOneSeatBothSatelliteSystemsShareRatherThanOneEach()
    {
        TunerCapacity capacity = await ReadAsync(
            [Wanted("adapter0")],
            [new TunerSnapshot("adapter0", TunerKind.Satellite, TunerState.Idle)]);

        TunerSeat seat = Assert.Single(capacity.Seats);

        Assert.Equal([TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110], seat.Serves);
        Assert.True(capacity.CanServe(TuneSystem.IsdbSBs));
        Assert.True(capacity.CanServe(TuneSystem.IsdbSCs110));
    }

    [Fact]
    public async Task TwoSatelliteTunersAreTwoSeatsAndNotFour()
    {
        TunerCapacity capacity = await ReadAsync(
            [Wanted("adapter0"), Wanted("adapter1")],
            [
                new TunerSnapshot("adapter0", TunerKind.Satellite, TunerState.Idle),
                new TunerSnapshot("adapter1", TunerKind.Satellite, TunerState.Idle),
            ]);

        Assert.Equal(2, capacity.Seats.Count);
        Assert.Equal([TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110], capacity.Served);
    }

    [Fact]
    public async Task ATunerTakenOutOfServiceHoldsNoSeat()
    {
        TunerCapacity capacity = await ReadAsync(
            [Wanted("adapter0"), new TunerConfigEntry { DeviceId = "adapter1", Disabled = true }],
            [
                new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle),
                new TunerSnapshot("adapter1", TunerKind.Satellite, TunerState.Disabled),
            ]);

        Assert.Equal("adapter0", Assert.Single(capacity.Seats).DeviceId);
        Assert.False(capacity.CanServe(TuneSystem.IsdbSBs));
        Assert.Empty(capacity.Undetermined);
    }

    [Fact]
    public async Task ATunerTheDriverNeverDescribedIsUndeterminedRatherThanAbsent()
    {
        TunerCapacity capacity = await ReadAsync([Wanted("adapter0")], []);

        Assert.Empty(capacity.Seats);
        Assert.Equal("adapter0", Assert.Single(capacity.Undetermined));
    }

    [Fact]
    public async Task ATunerThatNeverSaidWhatItReceivesIsUndeterminedRatherThanASeatServingNothing()
    {
        TunerCapacity capacity = await ReadAsync(
            [Wanted("adapter0")],
            [new TunerSnapshot("adapter0", TunerKind.Unspecified, TunerState.Idle)]);

        Assert.Empty(capacity.Seats);
        Assert.Equal("adapter0", Assert.Single(capacity.Undetermined));
    }

    [Fact]
    public async Task AFaultedTunerKeepsItsSeatAndSaysSo()
    {
        TunerCapacity capacity = await ReadAsync(
            [Wanted("adapter0")],
            [new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Faulted)]);

        TunerSeat seat = Assert.Single(capacity.Seats);

        Assert.True(seat.Faulted);
        Assert.True(capacity.CanServe(TuneSystem.IsdbT));
    }

    [Fact]
    public async Task AnIdleTunerIsNotFaulted()
    {
        TunerCapacity capacity = await ReadAsync(
            [Wanted("adapter0")],
            [new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle)]);

        Assert.False(Assert.Single(capacity.Seats).Faulted);
    }

    [Fact]
    public async Task ALedgerThatCannotBeReadIsUnknownRatherThanEmpty()
    {
        var driver = new LedgerOnlyDriverClient
        {
            Ledger = DriverCall<TunerLedgerDto>.Unreachable("no socket"),
        };

        Assert.Null(await new TunerCapacityDirectory(driver).ReadAsync(Cancel));
    }

    [Fact]
    public async Task TunersThatCannotBeReadLeaveTheCapacityUnknownRatherThanUndetermined()
    {
        var driver = new LedgerOnlyDriverClient
        {
            Ledger = DriverCall<TunerLedgerDto>.Reached(new TunerLedgerDto { Tuners = [Wanted("adapter0")] }),
            Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Unreachable("no socket"),
        };

        Assert.Null(await new TunerCapacityDirectory(driver).ReadAsync(Cancel));
    }

    [Fact]
    public async Task SeatsComeBackInAStableOrder()
    {
        TunerCapacity capacity = await ReadAsync(
            [Wanted("adapter2"), Wanted("adapter0"), Wanted("adapter1")],
            [
                new TunerSnapshot("adapter2", TunerKind.Terrestrial, TunerState.Idle),
                new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle),
                new TunerSnapshot("adapter1", TunerKind.Terrestrial, TunerState.Idle),
            ]);

        Assert.Equal(
            ["adapter0", "adapter1", "adapter2"],
            capacity.Seats.Select(seat => seat.DeviceId));
    }

    private static TunerConfigEntry Wanted(string deviceId) => new() { DeviceId = deviceId };

    private static async Task<TunerCapacity> ReadAsync(
        IReadOnlyList<TunerConfigEntry> ledger,
        IReadOnlyList<TunerSnapshot> tuners)
    {
        var driver = new LedgerOnlyDriverClient
        {
            Ledger = DriverCall<TunerLedgerDto>.Reached(new TunerLedgerDto { Tuners = ledger }),
            Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Reached(tuners),
        };

        return await new TunerCapacityDirectory(driver).ReadAsync(Cancel)
               ?? throw new InvalidOperationException("The capacity was expected to be known.");
    }
}
