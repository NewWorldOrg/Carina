using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class CompletionScramblingTests
{
    [Theory]
    [InlineData(QualityLevel.Warning)]
    [InlineData(QualityLevel.MayNotBeWatchable)]
    public void ARecordingLeftScrambledPastTheLevelIsClassifiedAsSuch(QualityLevel leftScrambled)
    {
        RecordingVerdict verdict = CompletionFactory.Judge(Scrambled(leftScrambled));

        Assert.Equal([RecordingFault.ScramblingUnresolved], verdict.Faults);
    }

    [Theory]
    [InlineData(QualityLevel.Good)]
    [InlineData(QualityLevel.Unmeasured)]
    public void ARecordingNotLeftScrambledPastTheLevelNamesNoScrambling(QualityLevel leftScrambled)
    {
        RecordingVerdict verdict = CompletionFactory.Judge(Scrambled(leftScrambled));

        Assert.Empty(verdict.Faults);
    }

    [Fact]
    public void ScramblingIsAClassificationBesideTheOutcomeRatherThanAnotherOutcome()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(Scrambled(QualityLevel.MayNotBeWatchable));

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
    }

    [Fact]
    public void ScramblingIsNamedBesideWhateverElseWentWrong()
    {
        RecordingEvidence evidence = new(
            CompletionFactory.TypicalBytes,
            TimeSpan.FromSeconds(900),
            CompletionFactory.WindowStart,
            CompletionFactory.WindowEnd,
            CompletionFactory.WindowEnd,
            QualityLevel.Warning);

        RecordingVerdict verdict = CompletionFactory.Judge(evidence);

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFault.ShortOfTheWindow));
        Assert.True(verdict.Names(RecordingFault.ScramblingUnresolved));
    }

    [Fact]
    public void EvidenceThatSaysNothingOfScramblingReadsAsUnmeasured()
    {
        Assert.Equal(QualityLevel.Unmeasured, CompletionFactory.Evidence().LeftScrambled);
    }

    private static RecordingEvidence Scrambled(QualityLevel leftScrambled)
        => new(
            CompletionFactory.TypicalBytes,
            CompletionFactory.WholeWindow,
            CompletionFactory.WindowStart,
            CompletionFactory.WindowEnd,
            CompletionFactory.WindowEnd,
            leftScrambled);
}
