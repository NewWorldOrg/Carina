using Carina.Domain.Migration;

using static Carina.Domain.Tests.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Migration;

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
            AsBroadcast(7, "one.m2ts", 17_171_113_480),
            null);

        Assert.Equal(MigrationRefusal.FileMissing, judged.Refusal);
        Assert.Equal(17_171_113_480, judged.Claimed);
        Assert.Null(judged.Observed);
    }

    [Fact]
    public void ARecordingWhoseFileIsThereButEmptyIsNotCarried()
    {
        MigrationVerdict judged = MigrationClassifier.OnARecording(
            Recording(7),
            AsBroadcast(7, "one.m2ts", 17_171_113_480),
            OnDisk("one.m2ts", 0));

        Assert.Equal(MigrationRefusal.ReallyEmpty, judged.Refusal);
        Assert.Equal(17_171_113_480, judged.Claimed);
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
        MigrationVerdict judged = MigrationClassifier.OnAFile(OnDisk("bash.sh", 539), null);

        Assert.Equal(MigrationRefusal.Orphan, judged.Refusal);
        Assert.Equal(MigrationPopulation.RecordingFiles, judged.Population);
        Assert.Equal("bash.sh", judged.Subject);
        Assert.Equal(539, judged.Observed);
        Assert.Null(judged.Claimed);
    }

    [Fact]
    public void AHalfWrittenFileNoRowNamesIsAnOrphanTooWhateverItIsCalled()
    {
        MigrationVerdict judged = MigrationClassifier.OnAFile(OnDisk("one.m2ts.tmp", 2_514_911_344), null);

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
            AsBroadcast(7, "one.m2ts", 5_208_028_640));

        Assert.Equal(MigrationRefusal.ReallyEmpty, judged.Refusal);
        Assert.Equal(5_208_028_640, judged.Claimed);
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
        SourceRule rule = new(
            3,
            "a rule",
            true,
            [],
            usesRegularExpression,
            caseSensitive,
            false,
            false,
            false,
            false,
            false);

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
        SourceRule rule = new(
            3,
            "a rule",
            true,
            [],
            false,
            false,
            recordsAtATimeOfDay,
            boundsTheDuration,
            boundsThePeriod,
            namesItsOwnDestination,
            namesItsOwnEncodeSettings);

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
}
