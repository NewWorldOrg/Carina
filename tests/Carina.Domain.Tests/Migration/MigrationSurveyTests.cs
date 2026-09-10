using Carina.Domain.Migration;

using static Carina.Domain.Tests.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationSurveyTests
{
    [Fact]
    public void EveryPopulationThatWasReadIsOfferedForJudgement()
    {
        MigrationRoll rolled = MigrationClassifier.Over(
            Ledger(
                recordings: [Recording(7)],
                files: [AsBroadcast(7, "one.m2ts", 100)],
                rules: [Rule(3)],
                reservations: [Reservation(5, fromARule: true)],
                channels: [Channel(11, SourceBroadcastKind.Terrestrial, InReach)]),
            [OnDisk("one.m2ts", 100)],
            Rescanned(InReach));

        Assert.Equal(MigrationPopulations.Counted, rolled.Populations);
        Assert.Equal(5, rolled.Verdicts.Count);
    }

    [Fact]
    public void TheWholeOfTheOutputDirectoryIsWalkedAndWhatNoRowNamesIsWrittenDown()
    {
        MigrationRoll rolled = MigrationClassifier.Over(
            Ledger(recordings: [Recording(7)], files: [AsBroadcast(7, "one.m2ts", 100)]),
            [OnDisk("one.m2ts", 100), OnDisk("notes.txt", 1_024), OnDisk("stray.m2ts", 700_000_000)],
            Rescanned(InReach));

        IReadOnlyList<MigrationVerdict> orphans =
            [.. rolled.Verdicts.Where(verdict => verdict.Refusal is MigrationRefusal.Orphan)];

        Assert.Equal(["notes.txt", "stray.m2ts"], orphans.Select(verdict => verdict.Subject));
        Assert.Equal(3, rolled.OfferedIn(MigrationPopulation.RecordingFiles));
    }

    [Fact]
    public void AnOrphanIsWrittenDownAndNotCarried()
    {
        MigrationRoll rolled = MigrationClassifier.Over(
            Ledger(),
            [OnDisk("notes.txt", 1_024)],
            Rescanned());

        Assert.Single(rolled.Verdicts);
        Assert.All(rolled.Verdicts, verdict => Assert.False(verdict.Carried));
        Assert.Equal(1, rolled.OfferedIn(MigrationPopulation.RecordingFiles));
    }

    [Fact]
    public void ARecordingIsNeverAnsweredByAFileOfAnotherName()
    {
        MigrationRoll rolled = MigrationClassifier.Over(
            Ledger(recordings: [Recording(7)], files: [AsBroadcast(7, "one.m2ts", 100)]),
            [OnDisk("another.m2ts", 100)],
            Rescanned(InReach));

        MigrationVerdict recording = rolled.Verdicts.Single(
            verdict => verdict.Population is MigrationPopulation.Recordings);

        Assert.Equal(MigrationRefusal.FileMissing, recording.Refusal);
    }

    [Fact]
    public void ARecordingCannotNameTwoFilesAsBroadcast()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(() => MigrationClassifier.Over(
            Ledger(
                recordings: [Recording(7)],
                files: [AsBroadcast(7, "one.m2ts", 100), AsBroadcast(7, "two.m2ts", 100)]),
            [],
            Rescanned()));

        Assert.Contains("two files as broadcast", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoRowsCannotClaimTheSameFile()
    {
        Assert.Throws<ArgumentException>(() => MigrationClassifier.Over(
            Ledger(files: [AsBroadcast(7, "one.m2ts", 100), Encoded(8, "one.m2ts", 100)]),
            [],
            Rescanned()));
    }

    [Fact]
    public void ADirectoryCannotHoldTheSamePathTwice()
    {
        Assert.Throws<ArgumentException>(() => MigrationClassifier.Over(
            Ledger(),
            [OnDisk("one.m2ts", 100), OnDisk("one.m2ts", 100)],
            Rescanned()));
    }

    [Fact]
    public void JudgementsComeBackInAnOrderThatDoesNotMoveBetweenRuns()
    {
        MigrationRoll rolled = MigrationClassifier.Over(
            Ledger(
                recordings: [Recording(9), Recording(7)],
                files: [AsBroadcast(9, "b.m2ts", 100), AsBroadcast(7, "a.m2ts", 100)]),
            [OnDisk("b.m2ts", 100), OnDisk("a.m2ts", 100)],
            Rescanned(InReach));

        Assert.Equal(
            [
                (MigrationPopulation.Recordings, "7"),
                (MigrationPopulation.Recordings, "9"),
                (MigrationPopulation.RecordingFiles, "a.m2ts"),
                (MigrationPopulation.RecordingFiles, "b.m2ts"),
            ],
            rolled.Verdicts.Select(verdict => (verdict.Population, verdict.Subject)));
    }

    [Fact]
    public void AnEmptyFileNeverReachesTheNewSystemAndIsWrittenDownOnBothSidesOfTheSameFact()
    {
        MigrationRoll rolled = MigrationClassifier.Over(
            Ledger(recordings: [Recording(7)], files: [AsBroadcast(7, "one.m2ts", 17_000_000_000)]),
            [OnDisk("one.m2ts", 0)],
            Rescanned(InReach));

        Assert.Equal(2, rolled.Verdicts.Count);
        Assert.All(rolled.Verdicts, verdict => Assert.Equal(MigrationRefusal.ReallyEmpty, verdict.Refusal));
    }
}
