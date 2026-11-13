using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Unit.Quality;

[Trait("Category", "Unit")]
public sealed class QualityReadingTests
{
    [Fact]
    public void NothingCanBeMeasuredThatWasNotThereToMeasure()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityReading.Of(subjects: 2, measured: 3, beyondThreshold: 0));

    [Fact]
    public void NothingCanBeBeyondAThresholdThatWasNeverMeasuredAgainstIt()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityReading.Of(subjects: 4, measured: 2, beyondThreshold: 3));
}
