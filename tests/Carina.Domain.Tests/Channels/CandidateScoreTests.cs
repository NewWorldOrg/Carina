using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class CandidateScoreTests
{
    private static readonly DateTime Until = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime From = Until.AddDays(-7);

    [Fact]
    public void TheLockRateIsTheShareOfTheSamplesReadThatHeldALock()
    {
        CandidateScore score = CandidateScore.Of(400, 300, 21_500, 0.0002, From, Until, Until);

        Assert.Equal(0.75, score.LockRate);
        Assert.Equal(400, score.Samples);
        Assert.Equal(21_500, score.CarrierToNoiseLowestMilliDecibels);
        Assert.Equal(0.0002, score.BitErrorRateHighest);
        Assert.Equal(From, score.MeasuredFrom);
        Assert.Equal(Until, score.MeasuredUntil);
        Assert.Equal(Until, score.EvaluatedAt);
    }

    [Fact(DisplayName = "BR-QD-001: a score rests on at least one sample, because nothing read is not a clean reading")]
    public void AScoreRestsOnAtLeastOneSample()
        => Assert.Throws<ArgumentOutOfRangeException>(() => CandidateScore.Of(0, 0, null, null, From, Until, Until));

    [Fact]
    public void MoreLockedSamplesThanSamplesReadIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => CandidateScore.Of(10, 11, null, null, From, Until, Until));

    [Fact]
    public void ACandidateThatNeverLockedCarriesNoCarrierOrErrorFigure()
    {
        Assert.Throws<ArgumentException>(() => CandidateScore.Of(10, 0, 21_500, null, From, Until, Until));
        Assert.Throws<ArgumentException>(() => CandidateScore.Of(10, 0, null, 0.0, From, Until, Until));
    }

    [Fact]
    public void ACandidateThatNeverLockedStillHasAScoreOfNothingHeld()
        => Assert.Equal(0.0, CandidateScore.Of(10, 0, null, null, From, Until, Until).LockRate);

    [Fact]
    public void ANegativeErrorRateIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => CandidateScore.Of(10, 10, null, -0.1, From, Until, Until));

    [Fact]
    public void APeriodThatDoesNotEndAfterItBeginsIsRefused()
        => Assert.Throws<ArgumentException>(() => CandidateScore.Of(10, 10, null, null, Until, Until, Until));

    [Fact]
    public void AScoreEvaluatedBeforeItsPeriodEndsIsRefused()
        => Assert.Throws<ArgumentException>(
            () => CandidateScore.Of(10, 10, null, null, From, Until, Until.AddSeconds(-1)));

    [Fact]
    public void ALocalTimeIsRefused()
        => Assert.Throws<ArgumentException>(
            () => CandidateScore.Of(10, 10, null, null, DateTime.SpecifyKind(From, DateTimeKind.Local), Until, Until));
}
