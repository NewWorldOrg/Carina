using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class CompletionVerdictTests
{
    private static readonly IReadOnlyList<RecordingVerdict> Sweep =
    [
        CompletionFactory.Judge(),
        CompletionFactory.Judge(bytes: 0),
        CompletionFactory.Judge(bytes: null),
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
            Assert.NotEmpty(verdict.Faults);
        }
    }

    [Fact]
    public void AWeightAboveTheRangeIsTheOnlyThingACompleteRecordingMayCarry()
    {
        foreach (RecordingVerdict verdict in Sweep.Where(verdict => verdict.Outcome == RecordingOutcome.Complete))
        {
            Assert.DoesNotContain(verdict.Faults, fault => fault != RecordingFault.HeavierThanTheStream);
        }
    }

    [Fact]
    public void TheSweepReachesAllThreeWaysARecordingCanEnd()
        => Assert.Equal(
            Enum.GetValues<RecordingOutcome>().Order().ToArray(),
            Sweep.Select(verdict => verdict.Outcome).Distinct().Order().ToArray());

    [Fact]
    public void TheSweepReachesEveryFaultTheVerdictCanName()
        => Assert.Equal(
            CompletionFactory.FaultsTheCrossCheckNames.Order().ToArray(),
            Sweep.SelectMany(verdict => verdict.Faults).Distinct().Order().ToArray());

    [Fact]
    public void AVerdictNamesEachFaultOnce()
    {
        foreach (RecordingVerdict verdict in Sweep)
        {
            Assert.Equal(verdict.Faults.Distinct().Count(), verdict.Faults.Count);
        }
    }

    [Fact]
    public void AVerdictAnswersWhetherItNamedAFault()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 0);

        Assert.True(verdict.Names(RecordingFault.NothingLanded));
        Assert.False(verdict.Names(RecordingFault.SizeUnobserved));
    }

    [Fact]
    public void TheFaultsAVerdictCarriesAreASnapshotOfTheListItWasGiven()
    {
        List<RecordingFault> faults = [RecordingFault.ShortOfTheWindow];

        RecordingVerdict verdict = RecordingVerdict.Of(RecordingOutcome.Truncated, 0.96, faults);
        faults.Add(RecordingFault.HeavierThanTheStream);

        Assert.Equal([RecordingFault.ShortOfTheWindow], verdict.Faults);
    }

    [Fact]
    public void AVerdictNamesAnEndingTheLedgerHolds()
    {
        ArgumentOutOfRangeException refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => RecordingVerdict.Of((RecordingOutcome)99, 1.0, []));

        Assert.Equal("outcome", refusal.ParamName);
    }

    [Fact]
    public void AVerdictWithoutAListOfFaultsIsRefused()
    {
        ArgumentNullException refusal = Assert.Throws<ArgumentNullException>(
            () => RecordingVerdict.Of(RecordingOutcome.Complete, 1.0, null!));

        Assert.Equal("faults", refusal.ParamName);
    }

    [Fact]
    public void TheDetailAVerdictHandsTheLedgerNamesTheSameFaultsInTheSameOrder()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 1, asked: false);

        Assert.Equal(
            verdict.Faults,
            verdict.Detail(CompletionFactory.WindowEnd).Select(detail => detail.Fault).ToArray());
    }

    [Fact]
    public void AVerdictThatDidNotEndCompleteAndNamesNothingIsRefused()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => RecordingVerdict.Of(RecordingOutcome.Truncated, 0.9, []));

        Assert.Equal("faults", refusal.ParamName);
    }

    [Fact]
    public void AVerdictThatFailedAndNamesNothingIsRefused()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => RecordingVerdict.Of(RecordingOutcome.Failed, 0.1, []));

        Assert.Equal("faults", refusal.ParamName);
    }

    [Fact]
    public void AVerdictThatEndedCompleteMayNameNothing()
        => Assert.Empty(RecordingVerdict.Of(RecordingOutcome.Complete, 1.0, []).Faults);

    [Fact]
    public void EveryVerdictTheEvaluatorBuildsIsOneTheLedgerWouldAccept()
    {
        foreach (RecordingVerdict verdict in Sweep)
        {
            RecordingVerdict rebuilt = RecordingVerdict.Of(verdict.Outcome, verdict.Coverage, verdict.Faults);

            Assert.Equal(verdict.Faults, rebuilt.Faults);
        }
    }

    [Fact]
    public void TheDetailAVerdictHandsTheLedgerSaysHowMuchOfTheWindowWasWritten()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 2_400_000_000,
            written: TimeSpan.FromSeconds(960));

        Assert.Equal(
            ["covered 0.9600 of the window"],
            verdict.Detail(CompletionFactory.WindowEnd).Select(detail => detail.Note).ToArray());
    }

    [Fact]
    public void TheDetailAVerdictHandsTheLedgerSpellsAWholeWindowOut()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 0);

        Assert.Equal(
            ["covered 1.0000 of the window"],
            verdict.Detail(CompletionFactory.WindowEnd).Select(detail => detail.Note).ToArray());
    }

    [Fact]
    public void EveryDetailAVerdictHandsTheLedgerSaysSomething()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 1, asked: false);

        Assert.DoesNotContain(
            verdict.Detail(CompletionFactory.WindowEnd),
            detail => string.IsNullOrWhiteSpace(detail.Note));
    }

    [Fact]
    public void TheDetailAVerdictHandsTheLedgerCarriesTheMomentItWasNoticed()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 0);

        Assert.Equal(
            [CompletionFactory.WindowEnd],
            verdict.Detail(CompletionFactory.WindowEnd).Select(detail => detail.NoticedAt).ToArray());
    }

    [Fact]
    public void ADetailNoticedOnALocalClockIsRefused()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 0);

        Assert.Throws<ArgumentException>(
            () => verdict.Detail(DateTime.SpecifyKind(CompletionFactory.WindowEnd, DateTimeKind.Local)));
    }

    [Fact]
    public void ACompleteVerdictHandsTheLedgerNothingToExplain()
    {
        RecordingVerdict verdict = CompletionFactory.Judge();

        Assert.Empty(verdict.Detail(CompletionFactory.WindowEnd));
    }
}
