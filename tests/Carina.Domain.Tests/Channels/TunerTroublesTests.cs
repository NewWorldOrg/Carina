using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Channels;

public sealed class TunerTroublesTests
{
    [Fact(DisplayName = "BR-TD-004: a tuner the driver took out of service for not locking is one that cannot lock")]
    public void ATunerTheDriverTookOutOfServiceForNotLockingIsOneThatCannotLock()
    {
        TunerTrouble trouble = Assert.Single(TunerTroubles.Of([Faulted("adapter0", SessionRefusalTitles.NoLock)]));

        Assert.Equal(new TunerDeviceId("adapter0"), trouble.Tuner);
        Assert.Equal(TunerTroubleKind.NoLock, trouble.Kind);
        Assert.Equal("NoLock", trouble.Classification);
        Assert.Equal(TunerTroubles.CannotLockClassification, trouble.Classification);
    }

    [Theory(DisplayName = "BR-QD-018: a tuner taken out of service for another cause is in trouble of the kind the driver names")]
    [InlineData(TunerFaultKind.RepeatedTuneFailure, TunerTroubleKind.RepeatedTuneFailure)]
    [InlineData(TunerFaultKind.LedgerDisagrees, TunerTroubleKind.LedgerDisagrees)]
    [InlineData(TunerFaultKind.DeviceFailed, TunerTroubleKind.DeviceFailed)]
    [InlineData(TunerFaultKind.DeviceFailedAgain, TunerTroubleKind.DeviceFailedAgain)]
    [InlineData(TunerFaultKind.Unspecified, TunerTroubleKind.Faulted)]
    [InlineData((TunerFaultKind)99, TunerTroubleKind.Faulted)]
    public void ATunerTakenOutOfServiceForAnotherCauseIsInTroubleOfTheKindTheDriverNames(
        TunerFaultKind named,
        TunerTroubleKind expected)
    {
        TunerSnapshot faulted = Faulted("adapter0", SessionRefusalTitles.NoData);

        TunerTrouble trouble = Assert.Single(TunerTroubles.Of(
            [faulted with { Health = faulted.Health! with { FaultKind = named } }]));

        Assert.Equal(expected, trouble.Kind);
        Assert.True(TunerTroubles.TakesItOutOfService(trouble.Kind));
    }

    [Fact(DisplayName = "BR-QD-018: a tuner the driver still hands out but says is failing to tune is in that trouble")]
    public void ATunerTheDriverStillHandsOutButSaysIsFailingToTuneIsInThatTrouble()
    {
        TunerTrouble trouble = Assert.Single(TunerTroubles.Of([Degraded("adapter0", TunerDegradedKind.TuneFailing)]));

        Assert.Equal(TunerTroubleKind.TuneFailing, trouble.Kind);
        Assert.False(TunerTroubles.TakesItOutOfService(trouble.Kind));
    }

    [Fact(DisplayName = "BR-QD-018: a tuner the driver says is not quite well without saying why is degraded")]
    public void ATunerTheDriverSaysIsNotQuiteWellWithoutSayingWhyIsDegraded()
        => Assert.Equal(
            TunerTroubleKind.Degraded,
            Assert.Single(TunerTroubles.Of([Degraded("adapter0", TunerDegradedKind.Unspecified)])).Kind);

    [Fact(DisplayName = "BR-QD-018: a tuner whose health says faulted is taken out of service even while its standing lags")]
    public void ATunerWhoseHealthSaysFaultedIsTakenOutOfServiceEvenWhileItsStandingLags()
        => Assert.Equal(
            TunerTroubleKind.NoLock,
            Assert.Single(TunerTroubles.Of(
                [Faulted("adapter0", SessionRefusalTitles.NoLock) with { State = TunerState.Idle }])).Kind);

    [Fact(DisplayName = "BR-QD-018: a healthy tuner and one that says nothing of its health are in no trouble")]
    public void AHealthyTunerAndOneThatSaysNothingOfItsHealthAreInNoTrouble()
        => Assert.Empty(TunerTroubles.Of(
        [
            new TunerSnapshot("adapter0", TunerKind.Satellite, TunerState.Idle)
            {
                Health = new TunerHealthDto { Level = TunerHealthLevel.Healthy },
            },
            new TunerSnapshot("adapter3", TunerKind.Terrestrial, TunerState.Busy),
        ]));

    [Fact(DisplayName = "BR-QS-002: a tuner the user has turned off is in no trouble, whatever its health last said")]
    public void ATunerTheUserHasTurnedOffIsInNoTrouble()
        => Assert.Empty(TunerTroubles.Of(
        [
            Faulted("adapter0", SessionRefusalTitles.NoLock) with { State = TunerState.Disabled },
            Degraded("adapter2", TunerDegradedKind.TuneFailing) with { State = TunerState.Disabled },
        ]));

    [Fact(DisplayName = "BR-TD-004: each tuner in trouble is named once, in the order the driver listed them")]
    public void EachTunerInTroubleIsNamedOnceInTheOrderTheDriverListedThem()
    {
        IReadOnlyList<TunerTrouble> troubles = TunerTroubles.Of(
        [
            Faulted("adapter2", SessionRefusalTitles.NoLock),
            new TunerSnapshot("adapter3", TunerKind.Terrestrial, TunerState.Idle),
            Degraded("adapter0", TunerDegradedKind.TuneFailing),
            Faulted("adapter2", SessionRefusalTitles.NoLock),
        ]);

        Assert.Equal(
            [new TunerDeviceId("adapter2"), new TunerDeviceId("adapter0")],
            troubles.Select(trouble => trouble.Tuner));
    }

    [Theory(DisplayName = "BR-TD-004: a tuner with no name a device could carry is passed over rather than stopping the rest")]
    [InlineData("")]
    [InlineData("adapter/0")]
    public void ATunerWithNoNameADeviceCouldCarryIsPassedOver(string deviceId)
    {
        TunerTrouble trouble = Assert.Single(TunerTroubles.Of(
        [
            Faulted(deviceId, SessionRefusalTitles.NoLock),
            Faulted("adapter0", SessionRefusalTitles.NoLock),
        ]));

        Assert.Equal(new TunerDeviceId("adapter0"), trouble.Tuner);
    }

    [Theory(DisplayName = "BR-QD-018: only a classification spelled the way a kind of trouble is spelled names one")]
    [InlineData("NoLock", TunerTroubleKind.NoLock)]
    [InlineData("TuneFailing", TunerTroubleKind.TuneFailing)]
    [InlineData("DeviceFailedAgain", TunerTroubleKind.DeviceFailedAgain)]
    [InlineData("NoData", null)]
    [InlineData("nolock", null)]
    [InlineData("1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void OnlyAClassificationSpelledTheWayAKindIsSpelledNamesOne(string? classification, TunerTroubleKind? expected)
        => Assert.Equal(expected, TunerTroubles.Named(classification));

    private static TunerSnapshot Faulted(string deviceId, string? title)
        => new(deviceId, TunerKind.Satellite, TunerState.Faulted, Detail: "the device failed to receive")
        {
            Health = new TunerHealthDto
            {
                Level = TunerHealthLevel.Faulted,
                Detail = "the device failed to receive",
                FaultTitle = title,
                FaultKind = TunerFaultKind.RepeatedTuneFailure,
            },
        };

    private static TunerSnapshot Degraded(string deviceId, TunerDegradedKind kind)
        => new(deviceId, TunerKind.Satellite, TunerState.Idle)
        {
            Health = new TunerHealthDto { Level = TunerHealthLevel.Degraded, DegradedKind = kind },
        };
}
