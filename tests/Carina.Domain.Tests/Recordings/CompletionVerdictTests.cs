using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class CompletionVerdictTests
{
    private static readonly IReadOnlyList<RecordingVerdict> Sweep =
    [
        CompletionFactory.Judge(),
        CompletionFactory.Judge(bytes: 0),
        CompletionFactory.Judge(bytes: null),
        CompletionFactory.Judge(new RecordingEvidence(
            CompletionFactory.TypicalBytes,
            null,
            CompletionFactory.WindowStart,
            CompletionFactory.WindowEnd,
            CompletionFactory.WindowEnd)),
        CompletionFactory.Judge(new RecordingEvidence(
            CompletionFactory.TypicalBytes,
            CompletionFactory.WholeWindow,
            null,
            null,
            CompletionFactory.WindowEnd)),
        CompletionFactory.Judge(asked: false),
        CompletionFactory.Judge(bytes: 2_400_000_000, written: TimeSpan.FromSeconds(960)),
        CompletionFactory.Judge(bytes: 1_700_000_000),
        CompletionFactory.Judge(bytes: 3_300_000_001),
    ];

    [Fact]
    public void EveryRecordingThatDidNotEndCompleteSaysWhatWasWrongWithIt()
    {
        foreach (RecordingVerdict verdict in Sweep.Where(verdict => verdict.Outcome != RecordingOutcome.Complete))
        {
            Assert.NotEmpty(verdict.Findings);
        }
    }

    [Fact]
    public void AWeightAboveTheRangeIsTheOnlyThingACompleteRecordingMayCarry()
    {
        foreach (RecordingVerdict verdict in Sweep.Where(verdict => verdict.Outcome == RecordingOutcome.Complete))
        {
            Assert.DoesNotContain(verdict.Findings, finding => finding != RecordingFinding.HeavierThanTheStream);
        }
    }

    [Fact]
    public void TheSweepReachesAllThreeWaysARecordingCanEnd()
        => Assert.Equal(
            Enum.GetValues<RecordingOutcome>().Order().ToArray(),
            Sweep.Select(verdict => verdict.Outcome).Distinct().Order().ToArray());

    [Fact]
    public void TheSweepReachesEveryFindingAVerdictCanName()
        => Assert.Equal(
            Enum.GetValues<RecordingFinding>().Order().ToArray(),
            Sweep.SelectMany(verdict => verdict.Findings).Distinct().Order().ToArray());

    [Fact]
    public void AVerdictNamesEachFindingOnce()
    {
        foreach (RecordingVerdict verdict in Sweep)
        {
            Assert.Equal(verdict.Findings.Distinct().Count(), verdict.Findings.Count);
        }
    }

    [Fact]
    public void AVerdictAnswersWhetherItNamedAFinding()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 0);

        Assert.True(verdict.Names(RecordingFinding.NothingLanded));
        Assert.False(verdict.Names(RecordingFinding.SizeUnknown));
    }
}
