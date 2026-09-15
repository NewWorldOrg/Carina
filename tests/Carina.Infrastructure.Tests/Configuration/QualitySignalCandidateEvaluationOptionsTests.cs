using Carina.Infrastructure.Configuration;

namespace Carina.Infrastructure.Tests.Configuration;

public sealed class QualitySignalCandidateEvaluationOptionsTests
{
    [Fact]
    public void CandidatesAreEvaluatedOverAWeekUnlessToldOtherwise()
        => Assert.Equal(TimeSpan.FromDays(7), new QualitySignalOptions().Read().EvaluateCandidatesOver);

    [Fact]
    public void HowFarBackCandidatesAreEvaluatedIsReadAsADuration()
        => Assert.Equal(
            TimeSpan.FromDays(3),
            new QualitySignalOptions { EvaluateCandidatesOver = "3.00:00:00" }.Read().EvaluateCandidatesOver);

    [Fact]
    public void ReachingFurtherBackThanAQualityPeriodMaySpanIsRefused()
        => Assert.Throws<ArgumentException>(
            () => new QualitySignalOptions { EvaluateCandidatesOver = "400.00:00:00" }.Read());

    [Fact]
    public void EvaluatingOverNothingIsRefused()
        => Assert.Throws<ArgumentException>(
            () => new QualitySignalOptions { EvaluateCandidatesOver = "00:00:00" }.Read());
}
