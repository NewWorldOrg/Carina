using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityThresholdTests
{
    private static readonly DateTime Declared = new(2026, 8, 21, 3, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "every threshold this domain judges by is kept under a key it names")]
    public void EveryThresholdThisDomainJudgesByIsKeptUnderAKeyItNames()
    {
        QualityThreshold threshold = QualityThreshold.Declare(
            QualityThresholdKey.PacketsLostWarning,
            Threshold.Provisionally(0.0002, observations: 0, Declared));

        Assert.Equal(QualityThresholdKey.PacketsLostWarning, threshold.Key);
        Assert.True(threshold.Setting.Provisional);
        Assert.Null(threshold.UpdatedBy);
    }

    [Fact]
    public void AThresholdSomebodyMovedSaysWhoMovedIt()
    {
        QualityThreshold threshold = QualityThreshold.Rehydrate(
            QualityThresholdKey.SupplySilence,
            Threshold.Of(600, 900, provisional: true, observations: 4, Declared),
            "operator");

        Assert.Equal("operator", threshold.UpdatedBy);
        Assert.Equal(900, threshold.Setting.Current);
        Assert.Equal(600, threshold.Setting.Default);
    }

    [Fact]
    public void AKeyThisDomainDoesNotNameIsNoKey()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityThreshold.Declare(
            (QualityThresholdKey)99,
            Threshold.Provisionally(1, observations: 0, Declared)));

    [Fact(DisplayName = "BR-QD-023: a level set by hand is never one that stands on a measurement")]
    public void ALevelSetByHandIsNeverOneThatStandsOnAMeasurement()
        => Assert.Throws<ArgumentException>(() => QualityThreshold.Rehydrate(
            QualityThresholdKey.CarrierToNoiseFloor,
            Threshold.Of(15_000, 19_500, provisional: false, 240, Declared),
            null,
            byHand: true,
            Measured(19_500)));

    [Fact(DisplayName = "BR-QD-023: a level that is not provisional is the measurement it stands on")]
    public void ALevelThatIsNotProvisionalIsTheMeasurementItStandsOn()
    {
        Assert.Throws<ArgumentException>(() => QualityThreshold.Rehydrate(
            QualityThresholdKey.CarrierToNoiseFloor,
            Threshold.Of(15_000, 19_500, provisional: false, 240, Declared),
            null,
            byHand: false,
            null));
        Assert.Throws<ArgumentException>(() => QualityThreshold.Rehydrate(
            QualityThresholdKey.CarrierToNoiseFloor,
            Threshold.Of(15_000, 19_500, provisional: false, 240, Declared),
            null,
            byHand: false,
            Measured(19_000)));

        QualityThreshold measured = QualityThreshold.Rehydrate(
            QualityThresholdKey.CarrierToNoiseFloor,
            Threshold.Of(15_000, 19_500, provisional: false, 240, Declared),
            null,
            byHand: false,
            Measured(19_500));

        Assert.Equal(19_500, measured.Measurement!.Value);
        Assert.False(measured.ByHand);
    }

    [Fact(DisplayName = "BR-QD-023: only the signal levels carry a measurement")]
    public void OnlyTheSignalLevelsCarryAMeasurement()
        => Assert.Throws<ArgumentException>(() => QualityThreshold.Rehydrate(
            QualityThresholdKey.LockRate,
            Threshold.Of(0.99, 0.99, provisional: true, 0, Declared),
            null,
            byHand: false,
            Measured(0.99)));

    private static QualityThresholdMeasurement Measured(double value)
        => QualityThresholdMeasurement.Of(value, 240, 18, Declared.AddDays(-7), Declared, Declared);
}
