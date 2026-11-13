using Carina.Domain.Migration;

using static Carina.Domain.Tests.Unit.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class MigrationClassifierTests
{
    [Fact]
    public void ARecordingThatSaysNothingAboutWhereItCameFromIsNotCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnARecording(
            Recording(7, null),
            AsBroadcast(7, "one.m2ts", 100),
            OnDisk("one.m2ts", 100));

        Assert.False(judged.Carried);
        Assert.Equal(MigrationRefusal.Unidentifiable, judged.Refusal);
        Assert.Equal(MigrationPopulation.Recordings, judged.Population);
        Assert.Equal("7", judged.Subject);
    }

    [Fact]
    public void ARecordingNoFileIsNamedForIsNotCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnARecording(Recording(7), null, null);

        Assert.Equal(MigrationRefusal.FileMissing, judged.Refusal);
        Assert.Null(judged.Claimed);
        Assert.Null(judged.Observed);
    }

    [Fact]
    public void ARecordingWhoseOnlyFileIsAnEncodedCopyIsNotCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnARecording(Recording(7), null, OnDisk("one.mp4", 100));

        Assert.Equal(MigrationRefusal.FileMissing, judged.Refusal);
    }

    [Fact]
    public void ARecordingWhoseFileIsNotOnTheDiskIsNotCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnARecording(
            Recording(7),
            AsBroadcast(7, "one.m2ts", 17_000_000_000),
            null);

        Assert.Equal(MigrationRefusal.FileMissing, judged.Refusal);
        Assert.Equal(17_000_000_000, judged.Claimed);
        Assert.Null(judged.Observed);
    }

    [Fact]
    public void ARecordingWhoseFileIsThereButEmptyIsNotCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnARecording(
            Recording(7),
            AsBroadcast(7, "one.m2ts", 17_000_000_000),
            OnDisk("one.m2ts", 0));

        Assert.Equal(MigrationRefusal.ReallyEmpty, judged.Refusal);
        Assert.Equal(17_000_000_000, judged.Claimed);
        Assert.Equal(0, judged.Observed);
    }

    [Fact]
    public void ARecordingWithAFileOfItsOwnIsCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnARecording(
            Recording(7),
            AsBroadcast(7, "one.m2ts", 100),
            OnDisk("one.m2ts", 100));

        Assert.True(judged.Carried);
        Assert.Null(judged.Refusal);
        Assert.Equal(100, judged.Claimed);
        Assert.Equal(100, judged.Observed);
    }

    [Fact]
    public void AFileNoRowNamesIsAnOrphan()
    {
        MigrationVerdict judged = MigrationClassifier.OnAFile(OnDisk("notes.txt", 1_024), null);

        Assert.Equal(MigrationRefusal.Orphan, judged.Refusal);
        Assert.Equal(MigrationPopulation.RecordingFiles, judged.Population);
        Assert.Equal("notes.txt", judged.Subject);
        Assert.Equal(1_024, judged.Observed);
        Assert.Null(judged.Claimed);
    }

    [Fact]
    public void AHalfWrittenFileNoRowNamesIsAnOrphanTooWhateverItIsCalled()
    {
        MigrationVerdict judged = MigrationClassifier.OnAFile(OnDisk("one.m2ts.tmp", 2_500_000_000), null);

        Assert.Equal(MigrationRefusal.Orphan, judged.Refusal);
    }

    [Fact]
    public void AnEncodedCopyIsLeftForTheNewSystemToMakeItselfAgain()
    {
        MigrationVerdict judged = MigrationClassifier.OnAFile(
            OnDisk("one.mp4", 100),
            Encoded(7, "one.mp4", 100));

        Assert.Equal(MigrationRefusal.OutOfScope, judged.Refusal);
    }

    [Fact]
    public void AFileARowNamesButNothingLandedInIsNotCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnAFile(
            OnDisk("one.m2ts", 0),
            AsBroadcast(7, "one.m2ts", 5_000_000_000));

        Assert.Equal(MigrationRefusal.ReallyEmpty, judged.Refusal);
        Assert.Equal(5_000_000_000, judged.Claimed);
        Assert.Equal(0, judged.Observed);
    }

    [Fact]
    public void AFileARowNamesIsCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnAFile(
            OnDisk("one.m2ts", 100),
            AsBroadcast(7, "one.m2ts", 100));

        Assert.True(judged.Carried);
    }

    [Fact]
    public void ARuleNamingAChannelNothingAnsweredForIsNotCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnARule(
            RuleOver(3, InReach, Elsewhere),
            Rescanned(InReach));

        Assert.Equal(MigrationRefusal.Unidentifiable, judged.Refusal);
        Assert.Equal(MigrationPopulation.Rules, judged.Population);
        Assert.Equal("3", judged.Subject);
    }

    [Fact]
    public void ARuleThatNamesNoChannelAtAllAsksForEveryOne()
    {
        MigrationVerdict judged = MigrationClassifier.OnARule(Rule(3), Rescanned());

        Assert.True(judged.Carried);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ARuleWrittenInAShapeThisSystemsRulesDoNotTakeIsNotCarried(
        bool usesRegularExpression,
        bool caseSensitive)
    {
        SourceRule rule = Rule(
            3,
            Terms(),
            new SourceRuleReach(usesRegularExpression, caseSensitive, false, false, false, false, false));

        Assert.Equal(MigrationRefusal.Inexpressible, MigrationClassifier.OnARule(rule, Rescanned()).Refusal);
    }

    [Theory]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, false, false, true)]
    public void ARuleAskingForSomethingThisSystemDoesNotDoIsNotCarried(
        bool recordsAtATimeOfDay,
        bool boundsTheDuration,
        bool boundsThePeriod,
        bool namesItsOwnDestination,
        bool namesItsOwnEncodeSettings)
    {
        SourceRule rule = Rule(
            3,
            Terms(),
            new SourceRuleReach(
                false,
                false,
                recordsAtATimeOfDay,
                boundsTheDuration,
                boundsThePeriod,
                namesItsOwnDestination,
                namesItsOwnEncodeSettings));

        Assert.Equal(MigrationRefusal.NoSuchFeature, MigrationClassifier.OnARule(rule, Rescanned()).Refusal);
    }

    [Fact]
    public void ARuleOfPlainWordsIsCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnARule(RuleOver(3, InReach), Rescanned(InReach));

        Assert.True(judged.Carried);
    }

    [Fact]
    public void AReservationARuleMadeIsLeftForTheRuleToMakeAgain()
    {
        MigrationVerdict judged = MigrationClassifier.OnAReservation(Reservation(5, fromARule: true));

        Assert.Equal(MigrationRefusal.OutOfScope, judged.Refusal);
        Assert.Equal(MigrationPopulation.Reservations, judged.Population);
    }

    [Fact]
    public void AReservationSomebodyMadeByHandIsCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnAReservation(Reservation(5, fromARule: false));

        Assert.True(judged.Carried);
    }

    [Fact]
    public void AChannelOfAKindThisSystemHasNoTypeForIsNotCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnAChannelDefinition(
            Channel(11, SourceBroadcastKind.Sky, InReach),
            Rescanned(InReach));

        Assert.Equal(MigrationRefusal.Inexpressible, judged.Refusal);
        Assert.Equal(MigrationPopulation.ChannelDefinitions, judged.Population);
    }

    [Fact]
    public void AChannelTheRescanDidNotFindIsNotCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnAChannelDefinition(
            Channel(11, SourceBroadcastKind.Terrestrial, Elsewhere),
            Rescanned(InReach));

        Assert.Equal(MigrationRefusal.Unidentifiable, judged.Refusal);
    }

    [Theory]
    [InlineData(SourceBroadcastKind.Terrestrial)]
    [InlineData(SourceBroadcastKind.BroadcastSatellite)]
    [InlineData(SourceBroadcastKind.CommunicationSatellite)]
    public void AChannelTheRescanFoundAgainIsCarried(SourceBroadcastKind kind)
    {
        MigrationVerdict judged = MigrationClassifier.OnAChannelDefinition(
            Channel(11, kind, InReach),
            Rescanned(InReach));

        Assert.True(judged.Carried);
    }

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

    [Fact(DisplayName = "BR-MS-001: a recording the source system says ended on the instant it began is not carried, as a shape this system cannot take")]
    public void ARecordingThatLastsNoTimeIsNotCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnARecording(
            new SourceRecording(7, "a programme", Began, Began, InReach, Programme),
            AsBroadcast(7, "one.m2ts", 100),
            OnDisk("one.m2ts", 90));

        Assert.False(judged.Carried);
        Assert.Equal(MigrationRefusal.Inexpressible, judged.Refusal);
        Assert.Equal("a programme", judged.Note);
        Assert.Equal(100, judged.Claimed);
        Assert.Equal(90, judged.Observed);
    }

    [Fact(DisplayName = "BR-MS-001: a recording that lasts a single tick is carried")]
    public void ARecordingThatLastsATickIsCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnARecording(
            new SourceRecording(7, "a programme", Began, Began.AddTicks(1), InReach, Programme),
            AsBroadcast(7, "one.m2ts", 100),
            OnDisk("one.m2ts", 100));

        Assert.True(judged.Carried);
    }

    [Fact(DisplayName = "BR-MS-001: a recording that names no programme and lasts no time is not carried because nothing identifies it")]
    public void WhatIdentifiesARecordingIsAskedBeforeHowLongItLasts()
    {
        MigrationVerdict judged = MigrationClassifier.OnARecording(
            new SourceRecording(7, "a programme", Began, Began, InReach, null),
            AsBroadcast(7, "one.m2ts", 100),
            OnDisk("one.m2ts", 100));

        Assert.Equal(MigrationRefusal.Unidentifiable, judged.Refusal);
    }

    [Fact(DisplayName = "BR-MS-001: a recording that lasts no time and has no file on the disk is refused for the missing file, which is what stops it first")]
    public void AMissingFileIsAskedBeforeHowLongTheRecordingLasts()
    {
        MigrationVerdict judged = MigrationClassifier.OnARecording(
            new SourceRecording(7, "a programme", Began, Began, InReach, Programme),
            AsBroadcast(7, "one.m2ts", 100),
            null);

        Assert.Equal(MigrationRefusal.FileMissing, judged.Refusal);
    }

    [Fact(DisplayName = "BR-MD-001: the file of a recording that is carried is the one kept as broadcast, and an encoded copy beside it is not")]
    public void TheFileCarriedIsTheOneKeptAsBroadcast()
    {
        IReadOnlyDictionary<long, SourceRecordingFile> carried = MigrationClassifier.AsBroadcastByRecording(
            [Encoded(7, "one.mp4", 50), AsBroadcast(7, "one.m2ts", 100), Encoded(8, "two.mp4", 50)]);

        Assert.Single(carried);
        Assert.Equal("one.m2ts", carried[7].Path);
    }

    [Fact(DisplayName = "BR-MD-001: a recording the source system names two files as broadcast for is refused rather than carried twice")]
    public void TwoFilesAsBroadcastForOneRecordingAreRefused()
        => Assert.Throws<ArgumentException>(() => MigrationClassifier.AsBroadcastByRecording(
            [AsBroadcast(7, "one.m2ts", 100), AsBroadcast(7, "again.m2ts", 100)]));
}
