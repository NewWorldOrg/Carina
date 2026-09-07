using Carina.Domain.Migration;

using static Carina.Domain.Tests.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationRecordShapeTests
{
    [Fact]
    public void WhatAPopulationOfferedIsWhatItCarriedWhatItDidNotAndWhatNobodyCouldSay()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => MigrationTally.Rehydrate(Run, MigrationPopulation.Recordings, 5, 2, 2, 0));

        Assert.Contains("nobody could say", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 1)]
    [InlineData(0, 0, -1, 1)]
    [InlineData(0, 0, 1, -1)]
    public void ARunCountsNothingNegative(int offered, int carried, int notCarried, int unclassified)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MigrationTally.Rehydrate(
            Run,
            MigrationPopulation.Recordings,
            offered,
            carried,
            notCarried,
            unclassified));
    }

    [Fact]
    public void TheProgrammeGuideIsNeverCountedAsRowsOfItsOwn()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MigrationTally.Rehydrate(
            Run,
            MigrationPopulation.ProgrammeGuide,
            0,
            0,
            0,
            0));
    }

    [Fact]
    public void WhyNothingWasDoneAboutSomethingIsSettledByTheRequirementsAndNotByTheRun()
    {
        Assert.Throws<ArgumentException>(() => MigrationOmission.Rehydrate(
            Run,
            MigrationOmissionSubject.ProgrammeGuide,
            MigrationOmissionGround.NothingToCarry));
    }

    [Theory]
    [InlineData(MigrationOmissionSubject.ProgrammeGuide, MigrationOmissionGround.NotMigratedByDesign)]
    [InlineData(MigrationOmissionSubject.DuplicateAvoidance, MigrationOmissionGround.NotMigratedByDesign)]
    [InlineData(MigrationOmissionSubject.QualityTimeSeries, MigrationOmissionGround.NothingToCarry)]
    [InlineData(MigrationOmissionSubject.RecordingHistory, MigrationOmissionGround.NotMigratedByDesign)]
    public void EachThingLeftAloneIsLeftAloneForTheReasonTheRequirementsGive(
        MigrationOmissionSubject subject,
        MigrationOmissionGround ground)
    {
        Assert.Equal(ground, MigrationOmission.For(Run, subject).Ground);
    }

    [Fact]
    public void AJudgementThatCarriedHasNoLineAmongWhatWasLeftBehind()
    {
        Assert.Throws<ArgumentException>(
            () => MigrationDetail.Of(Run, Carried(MigrationPopulation.Recordings, "7")));
    }

    [Fact]
    public void ARunFinishesAfterItStarts()
    {
        Assert.Throws<ArgumentException>(
            () => MigrationRun.Rehydrate(Run, Source, MigrationPass.Rehearsal, Ended, Began));
    }

    [Fact]
    public void ARunKeepsItsTimesInOneTimeZone()
    {
        Assert.Throws<ArgumentException>(() => MigrationRun.Rehydrate(
            Run,
            Source,
            MigrationPass.Rehearsal,
            DateTime.SpecifyKind(Began, DateTimeKind.Local),
            Ended));
    }

    [Fact]
    public void ARunIsEitherARehearsalOrTheRealThing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MigrationRun.Rehydrate(Run, Source, (MigrationPass)9, Began, Ended));
    }

    [Fact]
    public void ARunIdIsNeverEmpty()
    {
        Assert.Throws<ArgumentException>(() => new MigrationRunId(Guid.Empty));
    }

    [Fact]
    public void ADetailIdIsNeverEmpty()
    {
        Assert.Throws<ArgumentException>(() => new MigrationDetailId(Guid.Empty));
    }

    [Fact]
    public void AReasonIsOneTheRecordCanName()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MigrationVerdict.Refuse(
            MigrationPopulation.Recordings,
            "7",
            (MigrationRefusal)99,
            "a programme",
            null,
            null));
    }

    [Fact]
    public void AJudgementNamesWhatItIsAbout()
    {
        Assert.Throws<ArgumentException>(
            () => MigrationVerdict.Carry(MigrationPopulation.Recordings, string.Empty, "a programme", null, null));
    }

    [Fact]
    public void AJudgementWeighsNothingSmallerThanEmpty()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MigrationVerdict.Carry(
            MigrationPopulation.Recordings,
            "7",
            "a programme",
            null,
            -1));
    }
}
