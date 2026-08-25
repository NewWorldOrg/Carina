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

    public static readonly IReadOnlyList<RecordingFault> FaultsOnlyTheSupervisorCanName =
    [
        RecordingFault.TuneFailed,
        RecordingFault.RefusedByDiskPrecheck,
        RecordingFault.DiskExhausted,
        RecordingFault.DriverLost,
        RecordingFault.DrainGraceExpired,
        RecordingFault.StoppedByHand,
        RecordingFault.TunerContended,
        RecordingFault.ScramblingUnresolved,
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
            Enum.GetValues<RecordingFault>().Except(FaultsOnlyTheSupervisorCanName).Order().ToArray(),
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
