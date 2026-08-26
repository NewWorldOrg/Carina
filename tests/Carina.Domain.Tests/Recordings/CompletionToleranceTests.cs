using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class CompletionToleranceTests
{
    [Fact]
    public void TheToleranceTheLedgerStartsWithIsTheOneThatWasDecided()
    {
        Assert.Equal(0.995, CompletionTolerance.Default.CompleteCoverage, 12);
        Assert.Equal(0.95, CompletionTolerance.Default.TruncatedCoverage, 12);
        Assert.Equal(10, CompletionTolerance.Default.SizeSlackPercent);
    }

    [Fact]
    public void ARecordingIsJudgedByTheToleranceItWasHanded()
    {
        RecordingEvidence evidence = CompletionFactory.Evidence(written: TimeSpan.FromSeconds(970));

        RecordingVerdict strict = CompletionEvaluator.Judge(
            evidence,
            CompletionFactory.Bitrate,
            new CompletionTolerance(0.995, 0.95, 10));
        RecordingVerdict lenient = CompletionEvaluator.Judge(
            evidence,
            CompletionFactory.Bitrate,
            new CompletionTolerance(0.96, 0.5, 10));

        Assert.Equal(RecordingOutcome.Truncated, strict.Outcome);
        Assert.Equal(RecordingOutcome.Complete, lenient.Outcome);
    }

    [Fact]
    public void TheWarningFloorIsReadFromTheToleranceItWasHanded()
    {
        RecordingEvidence evidence = CompletionFactory.Evidence(
            bytes: 2_250_000_000,
            written: TimeSpan.FromSeconds(900));

        RecordingVerdict strict = CompletionEvaluator.Judge(
            evidence,
            CompletionFactory.Bitrate,
            new CompletionTolerance(0.995, 0.95, 10));
        RecordingVerdict lenient = CompletionEvaluator.Judge(
            evidence,
            CompletionFactory.Bitrate,
            new CompletionTolerance(0.995, 0.8, 10));

        Assert.Equal(RecordingOutcome.Failed, strict.Outcome);
        Assert.Equal(RecordingOutcome.Truncated, lenient.Outcome);
    }

    [Fact]
    public void TheSlackOnWeightMovesWhatTheOutcomeIs()
    {
        RecordingEvidence evidence = CompletionFactory.Evidence(bytes: 1_700_000_000);

        RecordingVerdict tight = CompletionFactory.JudgeBy(evidence, new CompletionTolerance(0.995, 0.95, 10));
        RecordingVerdict loose = CompletionFactory.JudgeBy(evidence, new CompletionTolerance(0.995, 0.95, 20));

        Assert.Equal(RecordingOutcome.Truncated, tight.Outcome);
        Assert.Equal(RecordingOutcome.Complete, loose.Outcome);
    }

    [Fact]
    public void ACoverageOfNothingIsNotAPassingMark()
    {
        ArgumentOutOfRangeException refusal =
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompletionTolerance(0, 0, 10));

        Assert.Equal("completeCoverage", refusal.ParamName);
    }

    [Fact]
    public void ACoverageOfMoreThanAWholeWindowIsRefused()
    {
        ArgumentOutOfRangeException refusal =
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompletionTolerance(1.001, 0.95, 10));

        Assert.Equal("completeCoverage", refusal.ParamName);
    }

    [Fact]
    public void AWholeWindowIsAllowedAsThePassingMark()
    {
        var tolerance = new CompletionTolerance(1, 0.95, 10);

        Assert.Equal(1.0, tolerance.CompleteCoverage, 12);
    }

    [Fact]
    public void AWarningFloorOfNothingIsRefused()
    {
        ArgumentOutOfRangeException refusal =
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompletionTolerance(0.995, 0, 10));

        Assert.Equal("truncatedCoverage", refusal.ParamName);
    }

    [Fact]
    public void AWarningFloorAboveThePassingMarkIsRefused()
    {
        ArgumentOutOfRangeException refusal =
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompletionTolerance(0.95, 0.96, 10));

        Assert.Equal("truncatedCoverage", refusal.ParamName);
    }

    [Fact]
    public void AWarningFloorAtThePassingMarkLeavesNoWarningBandAndIsAllowed()
    {
        var tolerance = new CompletionTolerance(0.99, 0.99, 0);

        Assert.Equal(0.99, tolerance.CompleteCoverage, 12);
        Assert.Equal(0.99, tolerance.TruncatedCoverage, 12);
    }

    [Fact]
    public void ACoverageThatIsNotANumberIsRefused()
    {
        ArgumentOutOfRangeException refusal =
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompletionTolerance(double.NaN, 0.95, 10));

        Assert.Equal("completeCoverage", refusal.ParamName);
    }

    [Fact]
    public void AWarningFloorThatIsNotANumberIsRefused()
    {
        ArgumentOutOfRangeException refusal =
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompletionTolerance(0.995, double.NaN, 10));

        Assert.Equal("truncatedCoverage", refusal.ParamName);
    }

    [Fact]
    public void ASlackBelowNothingIsRefused()
    {
        ArgumentOutOfRangeException refusal =
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompletionTolerance(0.995, 0.95, -1));

        Assert.Equal("sizeSlackPercent", refusal.ParamName);
    }

    [Fact]
    public void ASlackThatSwallowsTheWholeRangeIsRefused()
    {
        ArgumentOutOfRangeException refusal =
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompletionTolerance(0.995, 0.95, 100));

        Assert.Equal("sizeSlackPercent", refusal.ParamName);
    }

    [Fact]
    public void NoSlackAtAllIsAllowedAndWeighsTheFileAgainstTheRangeItself()
    {
        RecordingVerdict verdict = CompletionEvaluator.Judge(
            CompletionFactory.Evidence(bytes: 1_999_999_999),
            CompletionFactory.Bitrate,
            new CompletionTolerance(0.995, 0.95, 0));

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFault.LighterThanTheStream));
    }
}
