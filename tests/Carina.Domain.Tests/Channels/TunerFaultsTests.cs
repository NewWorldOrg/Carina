using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Channels;

public sealed class TunerFaultsTests
{
    [Fact(DisplayName = "BR-TD-004: a tuner the driver took out of service for not locking is one that cannot lock")]
    public void ATunerTheDriverTookOutOfServiceForNotLockingIsOneThatCannotLock()
    {
        TunerFault fault = Assert.Single(TunerFaults.ThatCannotLock([Faulted("adapter0", SessionRefusalTitles.NoLock)]));

        Assert.Equal(new TunerDeviceId("adapter0"), fault.Tuner);
        Assert.Equal(TuneFailureKind.NoLock, fault.Failure);
        Assert.Equal(nameof(TuneFailureKind.NoLock), fault.Classification);
    }

    [Fact(DisplayName = "BR-TD-004: a tuner that locked and then delivered nothing is not one that cannot lock")]
    public void ATunerThatLockedAndThenDeliveredNothingIsNotOneThatCannotLock()
        => Assert.Empty(TunerFaults.ThatCannotLock([Faulted("adapter0", SessionRefusalTitles.NoData)]));

    [Fact(DisplayName = "BR-TD-004: a tuner faulted for a cause other than tuning is not one that cannot lock")]
    public void ATunerFaultedForACauseOtherThanTuningIsNotOneThatCannotLock()
        => Assert.Empty(TunerFaults.ThatCannotLock([Faulted("adapter0", null)]));

    [Fact(DisplayName = "BR-TD-004: a driver that names no fault title says nothing about locking")]
    public void ADriverThatNamesNoFaultTitleSaysNothingAboutLocking()
        => Assert.Empty(TunerFaults.ThatCannotLock(
            [new TunerSnapshot("adapter0", TunerKind.Satellite, TunerState.Faulted, Detail: "did not lock")]));

    [Theory(DisplayName = "BR-TD-004: a tuner in any standing but faulted is not one that cannot lock, whatever its health last said")]
    [InlineData(TunerState.Idle)]
    [InlineData(TunerState.Busy)]
    [InlineData(TunerState.Disabled)]
    [InlineData(TunerState.Draining)]
    [InlineData(TunerState.Unspecified)]
    public void ATunerInAnyStandingButFaultedIsNotOneThatCannotLock(TunerState state)
        => Assert.Empty(TunerFaults.ThatCannotLock(
            [Faulted("adapter0", SessionRefusalTitles.NoLock) with { State = state }]));

    [Fact(DisplayName = "BR-TD-004: each tuner that cannot lock is named once, in the order the driver listed them")]
    public void EachTunerThatCannotLockIsNamedOnceInTheOrderTheDriverListedThem()
    {
        IReadOnlyList<TunerFault> faults = TunerFaults.ThatCannotLock(
        [
            Faulted("adapter2", SessionRefusalTitles.NoLock),
            new TunerSnapshot("adapter3", TunerKind.Terrestrial, TunerState.Idle),
            Faulted("adapter0", SessionRefusalTitles.NoLock),
            Faulted("adapter2", SessionRefusalTitles.NoLock),
        ]);

        Assert.Equal([new TunerDeviceId("adapter2"), new TunerDeviceId("adapter0")], faults.Select(fault => fault.Tuner));
    }

    [Theory(DisplayName = "BR-TD-004: a tuner with no name a device could carry is passed over rather than stopping the rest")]
    [InlineData("")]
    [InlineData("adapter/0")]
    public void ATunerWithNoNameADeviceCouldCarryIsPassedOver(string deviceId)
    {
        TunerFault fault = Assert.Single(TunerFaults.ThatCannotLock(
        [
            Faulted(deviceId, SessionRefusalTitles.NoLock),
            Faulted("adapter0", SessionRefusalTitles.NoLock),
        ]));

        Assert.Equal(new TunerDeviceId("adapter0"), fault.Tuner);
    }

    private static TunerSnapshot Faulted(string deviceId, string? title)
        => new(deviceId, TunerKind.Satellite, TunerState.Faulted, Detail: "the device failed to receive")
        {
            Health = new TunerHealthDto
            {
                Level = TunerHealthLevel.Faulted,
                Detail = "the device failed to receive",
                FaultTitle = title,
            },
        };
}
