using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class CompletionWindowTests
{
    private static readonly DateTime Start = CompletionFactory.WindowStart;

    [Fact]
    public void AWindowThatEndsWhenItStartsIsRefusedRatherThanJudged()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Evidence(Start, Start));

        Assert.Equal("windowEnd", refusal.ParamName);
    }

    [Fact]
    public void AWindowThatEndsBeforeItStartsIsRefusedRatherThanJudged()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Evidence(Start, Start.AddTicks(-1)));

        Assert.Equal("windowEnd", refusal.ParamName);
    }

    [Fact]
    public void AWindowOneTickLongIsMeasuredRatherThanRefused()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(new RecordingEvidence(
            1,
            TimeSpan.FromTicks(1),
            Start,
            Start.AddTicks(1),
            Start));

        Assert.Equal(1.0, verdict.Coverage, 12);
    }

    [Fact]
    public void AWindowOfOneSecondIsJudgedTheSameWayAsALongOne()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(new RecordingEvidence(
            2_500_000,
            TimeSpan.FromSeconds(1),
            Start,
            Start.AddSeconds(1),
            Start.AddSeconds(1)));

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.Empty(verdict.Faults);
    }

    [Fact]
    public void TheWindowIsTheDistanceBetweenItsTwoEnds()
    {
        RecordingEvidence evidence = Evidence(Start, Start.AddSeconds(1000));

        Assert.Equal(TimeSpan.FromSeconds(1000), evidence.Window);
    }

    [Fact]
    public void CoverageIsWhatWasWrittenOverThatDistance()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 1_250_000_000,
            written: TimeSpan.FromSeconds(500));

        Assert.Equal(0.5, verdict.Coverage, 12);
    }

    private static RecordingEvidence Evidence(DateTime windowStart, DateTime windowEnd)
        => new(
            CompletionFactory.TypicalBytes,
            CompletionFactory.WholeWindow,
            windowStart,
            windowEnd,
            CompletionFactory.WindowEnd);
}
