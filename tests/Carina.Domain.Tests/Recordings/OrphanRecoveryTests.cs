using Carina.Domain.Programmes;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class OrphanRecoveryTests
{
    /// <summary>
    /// Every reading there is, written out rather than worked out, so the table the rule holds is
    /// what this test holds and not a second copy of the code under it.
    /// </summary>
    public static TheoryData<bool, bool, bool, OrphanTreatment> EverySighting => new()
    {
        { false, false, false, OrphanTreatment.MarkWhatWasLeftBehind },
        { false, false, true, OrphanTreatment.ResumeIntoTheSameFile },
        { false, true, false, OrphanTreatment.ReadoptTheSession },
        { false, true, true, OrphanTreatment.ReadoptTheSession },
        { true, false, false, OrphanTreatment.MarkWhatWasLeftBehind },
        { true, false, true, OrphanTreatment.ResumeIntoTheSameFile },
        { true, true, false, OrphanTreatment.MarkWhatWasLeftBehind },
        { true, true, true, OrphanTreatment.ResumeIntoTheSameFile },
    };

    public static TheoryData<long?> EverySize => new(null, 0L, 1L, 188L, 64L * 1024 * 1024 * 1024);

    [Theory]
    [MemberData(nameof(EverySighting))]
    public void EverySightingLandsOnOneOfTheThreeThingsThatMayBeDone(
        bool another,
        bool stands,
        bool onAir,
        OrphanTreatment expected)
        => Assert.Equal(expected, OrphanRecovery.For(new OrphanSighting(another, stands, onAir)));

    [Fact]
    public void TheDriverThatIsStillWritingItIsTheOneWhoseSessionIsTakenBackUp()
        => Assert.Equal(
            OrphanTreatment.ReadoptTheSession,
            OrphanRecovery.For(new OrphanSighting(
                DriverIsAnotherInstance: false,
                SessionStands: true,
                StillOnAir: true)));

    [Fact]
    public void ASessionThatIsGoneWhileTheBroadcastRunsIsCarriedOnRatherThanEnded()
        => Assert.Equal(
            OrphanTreatment.ResumeIntoTheSameFile,
            OrphanRecovery.For(new OrphanSighting(
                DriverIsAnotherInstance: true,
                SessionStands: false,
                StillOnAir: true)));

    [Fact]
    public void ASessionThatIsGoneAfterTheBroadcastEndedIsMarkedForWhatIsLeftOfIt()
        => Assert.Equal(
            OrphanTreatment.MarkWhatWasLeftBehind,
            OrphanRecovery.For(new OrphanSighting(
                DriverIsAnotherInstance: true,
                SessionStands: false,
                StillOnAir: false)));

    [Theory]
    [MemberData(nameof(EverySize))]
    public void NothingRecoveryCanWriteSaysARecordingIsComplete(long? weighed)
    {
        RecordingOutcome outcome = OrphanRecovery.WhatIsLeftOf(weighed);

        Assert.NotEqual(RecordingOutcome.Complete, outcome);
        Assert.Contains(outcome, OrphanRecovery.OutcomesItCanWrite);
    }

    [Fact]
    public void TheOutcomesRecoveryCanWriteAreEveryOutcomeButComplete()
        => Assert.Equal(
            [.. Enum.GetValues<RecordingOutcome>().Where(outcome => outcome is not RecordingOutcome.Complete)],
            OrphanRecovery.OutcomesItCanWrite);

    [Fact]
    public void AFileWithSomethingInItIsTruncatedAndOneWithNothingInItIsAFailure()
    {
        Assert.Equal(RecordingOutcome.Truncated, OrphanRecovery.WhatIsLeftOf(1));
        Assert.Equal(RecordingOutcome.Failed, OrphanRecovery.WhatIsLeftOf(0));
        Assert.Equal(RecordingOutcome.Failed, OrphanRecovery.WhatIsLeftOf(null));
    }

    [Theory]
    [MemberData(nameof(EverySize))]
    public void WhateverWasLeftBehindAlwaysSaysWhyItEndedThere(long? weighed)
    {
        Assert.NotEmpty(OrphanRecovery.WhyItEndedWhereItDid(driverIsAnotherInstance: false, weighed));
        Assert.NotEmpty(OrphanRecovery.WhyItEndedWhereItDid(driverIsAnotherInstance: true, weighed));
    }

    [Fact]
    public void ADriverThatWasReplacedAndASessionThatWentAwayAreToldApart()
    {
        Assert.Equal(
            RecordingFault.DriverReplaced,
            OrphanRecovery.WhyNothingWasWritingIt(driverIsAnotherInstance: true));
        Assert.Equal(
            RecordingFault.LeftRunningUnwatched,
            OrphanRecovery.WhyNothingWasWritingIt(driverIsAnotherInstance: false));
    }

    [Fact]
    public void AFileNothingCouldBeWeighedAgainstSaysSoOnTopOfWhyNothingWasWritingIt()
        => Assert.Equal(
            [RecordingFault.LeftRunningUnwatched, RecordingFault.SizeUnobserved],
            OrphanRecovery.WhyItEndedWhereItDid(driverIsAnotherInstance: false, null));

    [Fact]
    public void AnEmptyFileSaysNothingLandedOnTopOfWhyNothingWasWritingIt()
        => Assert.Equal(
            [RecordingFault.DriverReplaced, RecordingFault.NothingLanded],
            OrphanRecovery.WhyItEndedWhereItDid(driverIsAnotherInstance: true, 0));

    [Fact]
    public void AFileThatWasWeighedSaysOnlyWhyNothingWasWritingIt()
        => Assert.Equal(
            [RecordingFault.LeftRunningUnwatched],
            OrphanRecovery.WhyItEndedWhereItDid(driverIsAnotherInstance: false, 4_096));

    [Fact]
    public void NeitherReasonRecoveryWritesNeedsATunerToHaveBeenNamed()
    {
        Assert.DoesNotContain(RecordingFault.LeftRunningUnwatched, RecordingFaults.ThatReachedTheTuner);
        Assert.DoesNotContain(RecordingFault.DriverReplaced, RecordingFaults.ThatReachedTheTuner);
    }

    [Fact]
    public void BothReasonsAreOnesAnInterruptionMayCarry()
    {
        Assert.Contains(RecordingFault.LeftRunningUnwatched, RecordingFaults.ThatCanInterrupt);
        Assert.Contains(RecordingFault.DriverReplaced, RecordingFaults.ThatCanInterrupt);
    }

    [Theory]
    [InlineData(GuideStanding.Announced, true, true)]
    [InlineData(GuideStanding.Announced, false, false)]
    [InlineData(GuideStanding.NothingKnown, true, true)]
    [InlineData(GuideStanding.NothingKnown, false, false)]
    [InlineData(GuideStanding.NoLongerAnnounced, true, false)]
    [InlineData(GuideStanding.NoLongerAnnounced, false, false)]
    public void WhatIsOnTheAirIsWhatTheGuideStillAnnouncesInsideTheWindowThatWasPromised(
        GuideStanding guide,
        bool windowIsStillOpen,
        bool expected)
        => Assert.Equal(expected, OrphanRecovery.StillOnAir(guide, windowIsStillOpen));
}
