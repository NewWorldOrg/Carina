using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityStateTests
{
    [Fact(DisplayName = "BR-QS-001: quality has six answers and none of them is spelled with another")]
    public void QualityHasSixAnswersAndNoneOfThemIsSpelledWithAnother()
    {
        Assert.Equal(6, QualityStates.All.Count);
        Assert.Equal(QualityStates.All.Distinct(), QualityStates.All);
        Assert.Equal(QualityStates.All.Order(), Enum.GetValues<QualityState>().Order());
    }

    [Fact]
    public void SomethingMeasuredAndInsideTheThresholdIsGood()
        => Assert.Equal(QualityState.Good, QualityStates.Of(QualityReading.Of(subjects: 40, measured: 40, beyondThreshold: 0)));

    [Fact]
    public void OneMeasurementBeyondTheThresholdIsEnoughToSayTheWarningLevelWasReached()
        => Assert.Equal(
            QualityState.AtOrAboveWarning,
            QualityStates.Of(QualityReading.Of(subjects: 40, measured: 40, beyondThreshold: 1)));

    [Fact(DisplayName = "BR-QD-001: subjects nothing counted are unmeasured rather than good")]
    public void SubjectsNothingCountedAreUnmeasuredRatherThanGood()
        => Assert.Equal(QualityState.Unmeasured, QualityStates.Of(QualityReading.Of(subjects: 3514, measured: 0, beyondThreshold: 0)));

    [Fact]
    public void APeriodHoldingNothingToMeasureIsNotAPeriodThatMeasuredWell()
        => Assert.Equal(QualityState.NothingToMeasure, QualityStates.Of(QualityReading.Of(subjects: 0, measured: 0, beyondThreshold: 0)));

    [Fact(DisplayName = "BR-QD-009: a statistic the tuner does not keep is unsupported, not an error and not a zero")]
    public void AStatisticTheTunerDoesNotKeepIsUnsupported()
        => Assert.Equal(QualityState.Unsupported, QualityStates.Of(QualityReading.Unsupported()));

    [Fact(DisplayName = "BR-QD-007: subjects whose supply has stopped are unreachable, not unmeasured")]
    public void SubjectsWhoseSupplyHasStoppedAreUnreachableRatherThanUnmeasured()
    {
        QualityReading nothingArriving = QualityReading.NotSupplied(subjects: 4, measured: 0);
        QualityReading nothingCounted = QualityReading.Of(subjects: 4, measured: 0, beyondThreshold: 0);

        Assert.Equal(QualityState.Unreachable, QualityStates.Of(nothingArriving));
        Assert.Equal(QualityState.Unmeasured, QualityStates.Of(nothingCounted));
    }

    [Fact]
    public void SomethingAlreadyMeasuredStillReadsAsUnreachableOnceItsSupplyStops()
        => Assert.Equal(QualityState.Unreachable, QualityStates.Of(QualityReading.NotSupplied(subjects: 4, measured: 3)));

    [Fact]
    public void ATunerThatKeepsNoSuchStatisticIsUnsupportedWhateverElseIsTrueOfIt()
        => Assert.Equal(
            QualityState.Unsupported,
            QualityStates.Of(QualityReading.Of(supported: false, supplied: false, subjects: 0, measured: 0, beyondThreshold: 0)));

    [Fact]
    public void APartlyMeasuredPeriodAnswersFromWhatWasMeasuredAndStillSaysHowMuchWasNot()
    {
        QualityReading reading = QualityReading.Of(subjects: 31, measured: 4, beyondThreshold: 0);

        Assert.Equal(QualityState.Good, QualityStates.Of(reading));
        Assert.Equal(27, reading.Unmeasured);
    }

    [Fact]
    public void NothingCanBeMeasuredThatWasNotThereToMeasure()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityReading.Of(subjects: 2, measured: 3, beyondThreshold: 0));

    [Fact]
    public void NothingCanBeBeyondAThresholdThatWasNeverMeasuredAgainstIt()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityReading.Of(subjects: 4, measured: 2, beyondThreshold: 3));
}
