using Carina.Domain.Migration;

using static Carina.Domain.Tests.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationCensusTests
{
    [Fact]
    public void AnElementNobodyJudgedStopsTheRun()
    {
        Dictionary<MigrationPopulation, int> offered = Nothing();
        offered[MigrationPopulation.Recordings] = 2;

        MigrationUnclassifiedException stopped = Assert.Throws<MigrationUnclassifiedException>(
            () => Report(Roll(offered, Carried(MigrationPopulation.Recordings, "7"))));

        Assert.Contains("no judgement", stopped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnElementJudgedTwiceStopsTheRunToo()
    {
        Dictionary<MigrationPopulation, int> offered = Nothing();
        offered[MigrationPopulation.Recordings] = 2;

        Assert.Throws<MigrationUnclassifiedException>(() => Report(Roll(
            offered,
            Carried(MigrationPopulation.Recordings, "7"),
            Carried(MigrationPopulation.Recordings, "7"))));
    }

    [Fact]
    public void MoreJudgementsThanElementsStopsTheRun()
    {
        Dictionary<MigrationPopulation, int> offered = Nothing();

        Assert.Throws<MigrationUnclassifiedException>(
            () => Report(Roll(offered, Carried(MigrationPopulation.Recordings, "7"))));
    }

    [Fact]
    public void APopulationNobodyReadStopsTheRun()
    {
        Dictionary<MigrationPopulation, int> offered = Nothing();
        offered.Remove(MigrationPopulation.Rules);

        MigrationUnclassifiedException stopped = Assert.Throws<MigrationUnclassifiedException>(
            () => Report(Roll(offered)));

        Assert.Contains("never read", stopped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProgrammeGuideIsNeverASummaryRowOfItsOwn()
    {
        Dictionary<MigrationPopulation, int> offered = Nothing();
        offered[MigrationPopulation.ProgrammeGuide] = 1;

        Assert.Throws<ArgumentOutOfRangeException>(() => Report(Roll(
            offered,
            Carried(MigrationPopulation.ProgrammeGuide, "the guide"))));
    }

    [Fact]
    public void ARunThatExplainedEverythingLeavesNothingUnclassified()
    {
        Dictionary<MigrationPopulation, int> offered = Nothing();
        offered[MigrationPopulation.Recordings] = 2;

        MigrationReport told = Report(Roll(
            offered,
            Carried(MigrationPopulation.Recordings, "7"),
            Refused(MigrationPopulation.Recordings, "9", MigrationRefusal.ReallyEmpty)));

        Assert.All(told.Tallies, tally => Assert.Equal(0, tally.Unclassified));
        Assert.Equal(MigrationPopulations.Counted, told.Tallies.Select(tally => tally.Population));

        MigrationTally recordings = told.Tallies.Single(
            tally => tally.Population is MigrationPopulation.Recordings);

        Assert.Equal(2, recordings.Offered);
        Assert.Equal(1, recordings.Carried);
        Assert.Equal(1, recordings.NotCarried);
    }

    [Fact]
    public void EverythingLeftBehindIsWrittenDownWithItsOwnReasonAndNothingIsCutOff()
    {
        Dictionary<MigrationPopulation, int> offered = Nothing();
        offered[MigrationPopulation.RecordingFiles] = 400;

        MigrationVerdict[] verdicts =
        [
            .. Enumerable.Range(1, 400).Select(number => Refused(
                MigrationPopulation.RecordingFiles,
                $"stray-{number}.m2ts",
                MigrationRefusal.Orphan)),
        ];

        MigrationReport told = Report(Roll(offered, verdicts));

        Assert.Equal(400, told.Details.Count);
        Assert.Equal(400, told.Tallies
            .Single(tally => tally.Population is MigrationPopulation.RecordingFiles)
            .NotCarried);
    }

    [Fact]
    public void EveryReasonTheRunCanGiveSurvivesIntoTheRecord()
    {
        Dictionary<MigrationPopulation, int> offered = Nothing();
        offered[MigrationPopulation.Recordings] = MigrationRefusals.All.Count;

        MigrationVerdict[] verdicts =
        [
            .. MigrationRefusals.All.Select(refusal => Refused(
                MigrationPopulation.Recordings,
                refusal.ToString(),
                refusal)),
        ];

        MigrationReport told = Report(Roll(offered, verdicts));

        Assert.Equal(MigrationRefusals.All, told.Details.Select(detail => detail.Refusal).Order());
    }

    [Fact]
    public void WhatArrivedDiminishedIsAlwaysWrittenDownWhateverTheRunFound()
    {
        MigrationReport told = Report(Roll(Nothing()));

        Assert.Equal(
            MigrationLossSubjects.All,
            told.Losses.Select(loss => loss.Subject).Order());

        Assert.All(told.Losses, loss => Assert.Equal(0, loss.Affected));
    }

    [Fact]
    public void ARunSaysWhichSourceItReadAndWhetherItWasARehearsal()
    {
        MigrationReport told = MigrationCensus.Taken(
            Run,
            Source,
            MigrationPass.ForReal,
            Roll(Nothing()),
            MigrationAftermath.Nothing,
            Began,
            Ended);

        Assert.Equal(Source, told.Run.Source);
        Assert.Equal(MigrationPass.ForReal, told.Run.Pass);
        Assert.Equal(Began, told.Run.StartedAt);
        Assert.Equal(Ended, told.Run.FinishedAt);
    }

    [Fact]
    public void WhatALineOfTheRecordSaysIsWhatTheJudgementSaid()
    {
        Dictionary<MigrationPopulation, int> offered = Nothing();
        offered[MigrationPopulation.Recordings] = 1;

        MigrationReport told = Report(Roll(
            offered,
            MigrationVerdict.Refuse(
                MigrationPopulation.Recordings,
                "7",
                MigrationRefusal.ReallyEmpty,
                "a programme",
                17_171_113_480,
                0)));

        MigrationDetail line = Assert.Single(told.Details);

        Assert.Equal(Run, line.RunId);
        Assert.Equal(MigrationPopulation.Recordings, line.Population);
        Assert.Equal(MigrationRefusal.ReallyEmpty, line.Refusal);
        Assert.Equal("7", line.Subject);
        Assert.Equal("a programme", line.Note);
        Assert.Equal(17_171_113_480, line.Claimed);
        Assert.Equal(0, line.Observed);
    }
}
