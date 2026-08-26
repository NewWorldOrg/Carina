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
        TunerCapacity capacity = await ReadAsync([Wanted("adapter0", TunerKind.Satellite)], []);

        Assert.Equal(1, capacity.SeatCount);
        Assert.True(capacity.CanServe(TuneSystem.IsdbSBs));
        Assert.True(capacity.CanServe(TuneSystem.IsdbSCs110));
        Assert.False(capacity.CanSeat(
            new Dictionary<TuneSystem, int> { [TuneSystem.IsdbSBs] = 1, [TuneSystem.IsdbSCs110] = 1 }));
    }

    [Fact]
    public async Task WhatATunerReceivesIsReadFromTheSavedLedgerAndNotFromTheRunningDriver()
    {
        TunerCapacity capacity = await ReadAsync(
            [Wanted("adapter0", TunerKind.Satellite)],
            [new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle)]);

        Assert.True(capacity.CanServe(TuneSystem.IsdbSBs));
        Assert.False(capacity.CanServe(TuneSystem.IsdbT));
    }

    [Fact]
    public async Task ATunerSavedButNotYetLoadedByTheDriverStillHoldsItsSeat()
    {
        TunerCapacity capacity = await ReadAsync([Wanted("adapter0", TunerKind.Terrestrial)], []);

        Assert.Equal(1, capacity.SeatCount);
        Assert.True(capacity.CanServe(TuneSystem.IsdbT));
        Assert.Empty(capacity.Undetermined);
    }

    [Fact]
    public async Task TwoSatelliteTunersAreTwoSeatsAndNotFour()
    {
        TunerCapacity capacity = await ReadAsync(
            [Wanted("adapter0", TunerKind.Satellite), Wanted("adapter1", TunerKind.Satellite)],
            []);

        Assert.Equal(2, capacity.SeatCount);
        Assert.Equal([TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110], capacity.Reachable.Order());
    }

    [Fact]
    public async Task ATunerTakenOutOfServiceHoldsNoSeat()
    {
        TunerCapacity capacity = await ReadAsync(
            [
                Wanted("adapter0", TunerKind.Terrestrial),
                new TunerConfigEntry { DeviceId = "adapter1", Kind = TunerKind.Satellite, Disabled = true },
            ],
            []);

        Assert.Equal(1, capacity.SeatCount);
        Assert.False(capacity.CanServe(TuneSystem.IsdbSBs));
        Assert.Empty(capacity.Undetermined);
    }

    [Fact]
    public async Task ATunerAnOlderDriverNeverDescribedIsUndeterminedRatherThanAbsent()
    {
        TunerCapacity capacity = await ReadAsync([Wanted("adapter0", TunerKind.Unspecified)], []);

        Assert.Equal(0, capacity.SeatCount);
        Assert.Equal("adapter0", Assert.Single(capacity.Undetermined));
    }

    [Fact]
    public async Task AFaultedTunerKeepsItsSeatAndSaysSo()
    {
        TunerCapacity capacity = await ReadAsync(
            [Wanted("adapter0", TunerKind.Terrestrial)],
            [new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Faulted)]);

        Assert.Equal(1, capacity.SeatCount);
        Assert.Equal(0, capacity.Healthy.SeatCount);
    }

    [Fact]
    public async Task AnIdleTunerIsNotFaulted()
    {
        TunerCapacity capacity = await ReadAsync(
            [Wanted("adapter0", TunerKind.Terrestrial)],
            [new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle)]);

        Assert.Equal(1, capacity.Healthy.SeatCount);
    }

    [Theory]
    [InlineData(TunerState.Idle)]
    [InlineData(TunerState.Busy)]
    [InlineData(TunerState.Draining)]
    public async Task ATunerThatIsMerelyInUseIsNotABrokenOne(TunerState state)
    {
        TunerCapacity capacity = await ReadAsync(
            [Wanted("adapter0", TunerKind.Terrestrial)],
            [new TunerSnapshot("adapter0", TunerKind.Terrestrial, state)]);

        Assert.Equal(1, capacity.Healthy.SeatCount);
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
    public async Task TunersThatCannotBeReadLeaveTheLedgerSeatsStandingWithNothingCalledFaulted()
    {
        var driver = new LedgerOnlyDriverClient
        {
            Ledger = DriverCall<TunerLedgerDto>.Reached(
                new TunerLedgerDto { Tuners = [Wanted("adapter0", TunerKind.Terrestrial)] }),
            Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Unreachable("no socket"),
        };

        TunerCapacity capacity = await new TunerCapacityDirectory(driver).ReadAsync(Cancel)
                                 ?? throw new InvalidOperationException("The capacity was expected to be known.");

        Assert.Equal(1, capacity.SeatCount);
        Assert.Equal(1, capacity.Healthy.SeatCount);
    }

    [Fact]
    public async Task SeatsComeBackInAStableOrder()
    {
        TunerCapacity capacity = await ReadAsync(
            [
                Wanted("adapter2", TunerKind.Terrestrial),
                Wanted("adapter0", TunerKind.Unspecified),
                Wanted("adapter1", TunerKind.Unspecified),
            ],
            []);

        Assert.Equal(["adapter0", "adapter1"], capacity.Undetermined);
        Assert.Equal(1, capacity.SeatCount);
    }

    private static TunerConfigEntry Wanted(string deviceId, TunerKind kind)
        => new() { DeviceId = deviceId, Kind = kind };

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
