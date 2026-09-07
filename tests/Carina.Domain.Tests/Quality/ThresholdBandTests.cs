using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class ThresholdBandTests
{
    [Fact]
    public void ABandMayStandOnAWarningLevelAloneWhenNothingWorseHasBeenNamed()
    {
        ThresholdBand band = QualityFactory.WarningOnly();

        Assert.Equal(QualityThresholdKey.PacketsLostWarning, band.WarningKey);
        Assert.Null(band.UnwatchableKey);
        Assert.Null(band.Unwatchable);
    }

    [Fact]
    public void ACeilingPutsTheUnwatchableLevelAboveTheWarningLevel()
    {
        ThresholdBand band = QualityFactory.PacketsLost(warning: 0.0002, unwatchable: 0.001);

        Assert.Equal(0.0002, band.Warning.Current, 12);
        Assert.Equal(0.001, band.Unwatchable!.Current, 12);
    }

    [Fact]
    public void ACeilingWhoseUnwatchableLevelSitsBelowItsWarningLevelWouldNeverReadAsWarning()
        => Assert.Throws<ArgumentException>(() => QualityFactory.PacketsLost(warning: 0.001, unwatchable: 0.0002));

    [Fact]
    public void AFloorPutsTheUnwatchableLevelBelowTheWarningLevel()
    {
        ThresholdBand band = QualityFactory.LockRate(warning: 0.9, unwatchable: 0.5);

        Assert.Equal(ThresholdSense.Floor, band.Sense);
        Assert.Equal(0.5, band.Unwatchable!.Current, 12);
    }

    [Fact]
    public void AFloorWhoseUnwatchableLevelSitsAboveItsWarningLevelWouldNeverReadAsWarning()
        => Assert.Throws<ArgumentException>(() => QualityFactory.LockRate(warning: 0.5, unwatchable: 0.9));

    [Fact]
    public void TwoLevelsOfTheSameBandAreKeptUnderTwoDifferentKeys()
        => Assert.Throws<ArgumentException>(() => ThresholdBand.Of(
            ThresholdSense.Ceiling,
            QualityThresholdKey.PacketsLostWarning,
            QualityFactory.Provisional(0.0002),
            QualityThresholdKey.PacketsLostWarning,
            QualityFactory.Provisional(0.001)));

    [Fact]
    public void ABandIsKeptUnderKeysThisDomainNames()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ThresholdBand.Of(
            ThresholdSense.Ceiling,
            (QualityThresholdKey)99,
            QualityFactory.Provisional(0.0002)));

    [Fact]
    public void ABandIsReadInADirectionThisDomainNames()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ThresholdBand.Of(
            (ThresholdSense)0,
            QualityThresholdKey.PacketsLostWarning,
            QualityFactory.Provisional(0.0002)));

    [Fact(DisplayName = "BR-QD-003: a band standing on any provisional level is a provisional band")]
    public void ABandStandingOnAnyProvisionalLevelIsAProvisionalBand()
    {
        ThresholdBand halfSettled = ThresholdBand.Of(
            ThresholdSense.Ceiling,
            QualityThresholdKey.PacketsLostWarning,
            QualityFactory.Firm(0.0002),
            QualityThresholdKey.PacketsLostUnwatchable,
            QualityFactory.Provisional(0.001));

        Assert.True(halfSettled.Provisional);
        Assert.False(ThresholdBand.Of(
            ThresholdSense.Ceiling,
            QualityThresholdKey.PacketsLostWarning,
            QualityFactory.Firm(0.0002)).Provisional);
    }
}
