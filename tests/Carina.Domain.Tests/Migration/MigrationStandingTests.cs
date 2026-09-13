using Carina.Domain.Encodings;
using Carina.Domain.Migration;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationStandingTests
{
    private static readonly MigrationRunId Run = new(new Guid("00000001-0000-0000-0000-000000000001"));

    public static TheoryData<MigrationRootStanding> HowTheNewRootCanStand =>
        [.. Enum.GetValues<MigrationRootStanding>()];

    public static TheoryData<EncodeUnaskedStanding> HowWhereEncodesGoCanStand =>
        [.. Enum.GetValues<EncodeUnaskedStanding>()];

    public static TheoryData<MigrationCarryStanding> HowTheCarryCanStand =>
        [.. Enum.GetValues<MigrationCarryStanding>()];

    public static TheoryData<MigrationStandingSubject> Subjects => [.. Enum.GetValues<MigrationStandingSubject>()];

    public static TheoryData<MigrationFinding> Findings => [.. Enum.GetValues<MigrationFinding>()];

    [Fact]
    public void ARunSaysWhatItFoundAboutEverySubjectAndSaysItOnce()
    {
        IReadOnlyList<MigrationStanding> stood = Every(
            MigrationRootStanding.Empty,
            EncodeUnaskedStanding.Settled,
            MigrationCarryStanding.WouldBeAHardLink);

        Assert.Equal(MigrationStandingSubjects.All, stood.Select(standing => standing.Subject).Order());
    }

    [Theory]
    [MemberData(nameof(HowTheNewRootCanStand))]
    public void HowTheNewRootStandsIsWrittenDownWhicheverWayItStands(MigrationRootStanding standing)
        => Assert.Contains(
            MigrationFindings.Of(standing),
            MigrationFindings.Under(MigrationStandingSubject.TheNewRoot));

    [Theory]
    [MemberData(nameof(HowWhereEncodesGoCanStand))]
    public void WhereEncodesGoIsWrittenDownWhicheverWayItStands(EncodeUnaskedStanding standing)
        => Assert.Contains(
            MigrationFindings.Of(standing),
            MigrationFindings.Under(MigrationStandingSubject.WhereEncodesGo));

    [Theory]
    [MemberData(nameof(HowTheCarryCanStand))]
    public void WhatALinkIntoTheNewRootWouldMeetIsWrittenDownWhicheverWayItStands(MigrationCarryStanding standing)
        => Assert.Contains(
            MigrationFindings.Of(standing),
            MigrationFindings.Under(MigrationStandingSubject.CarryingIntoTheNewRoot));

    [Theory]
    [MemberData(nameof(Subjects))]
    public void EverySubjectHasFindingsAndNoneOfThemBelongToAnotherSubject(MigrationStandingSubject subject)
    {
        IReadOnlyList<MigrationFinding> under = MigrationFindings.Under(subject);

        Assert.NotEmpty(under);
        Assert.All(
            MigrationStandingSubjects.All.Where(other => other != subject),
            other => Assert.Empty(under.Intersect(MigrationFindings.Under(other))));
    }

    [Theory]
    [MemberData(nameof(Findings))]
    public void EveryFindingIsOneSomeSubjectCanComeBackWith(MigrationFinding finding)
        => Assert.Contains(finding, MigrationStandingSubjects.All.SelectMany(MigrationFindings.Under));

    [Fact]
    public void NothingInTheWayIsTheOnlyPairARunForRealWouldGetPast()
    {
        Assert.All(
            Every(
                MigrationRootStanding.Empty,
                EncodeUnaskedStanding.Settled,
                MigrationCarryStanding.WouldBeAHardLink),
            standing => Assert.False(standing.WouldStopARunForReal));

        Assert.All(
            Every(
                MigrationRootStanding.NotEmpty,
                EncodeUnaskedStanding.MoreThanOneIsOffered,
                MigrationCarryStanding.WouldCrossAMount),
            standing => Assert.True(standing.WouldStopARunForReal));
    }

    [Fact]
    public void ARunForRealIsNotStoppedByASourceThatHasNothingLeftToCarry()
        => Assert.False(
            Found(
                MigrationStandingSubject.CarryingIntoTheNewRoot,
                MigrationRootStanding.Empty,
                EncodeUnaskedStanding.Settled,
                MigrationCarryStanding.NothingIsThereToCarry)
                .WouldStopARunForReal);

    [Fact]
    public void AFindingThatBelongsToAnotherSubjectIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => MigrationStanding.Rehydrate(
            Run,
            MigrationStandingSubject.TheNewRoot,
            MigrationFinding.NothingSaysWhereEncodesGo));

    [Fact]
    public void EveryFindingBelongsToExactlyOneSubject()
    {
        List<MigrationFinding> under =
            [.. MigrationStandingSubjects.All.SelectMany(MigrationFindings.Under)];

        Assert.Equal(MigrationFindings.All.Count, under.Count);
        Assert.Equal(MigrationFindings.All.Order(), under.Order());
    }

    [Fact]
    public void EveryFindingIsEitherOneARunForRealGetsPastOrOneItStopsAt()
    {
        Assert.Equal(
            [
                MigrationFinding.TheNewRootIsEmpty,
                MigrationFinding.WhereEncodesGoIsSettled,
                MigrationFinding.TheCarryWouldBeAHardLink,
                MigrationFinding.NothingIsThereToCarry,
            ],
            MigrationFindings.All.Where(finding => !MigrationFindings.WouldStopARunForReal(finding)).Order());
    }

    [Fact]
    public void ASubjectOutsideTheOnesTheRecordKnowsIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MigrationStandingSubjects.Named((MigrationStandingSubject)9));
        Assert.Throws<ArgumentOutOfRangeException>(() => MigrationFindings.Named((MigrationFinding)99));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MigrationFindings.Of((MigrationRootStanding)9));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MigrationFindings.Of((EncodeUnaskedStanding)9));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MigrationFindings.Of((MigrationCarryStanding)9));
    }

    private static IReadOnlyList<MigrationStanding> Every(
        MigrationRootStanding newRoot,
        EncodeUnaskedStanding whereEncodesGo,
        MigrationCarryStanding carrying)
        => MigrationStanding.EveryOne(Run, newRoot, whereEncodesGo, carrying);

    private static MigrationStanding Found(
        MigrationStandingSubject subject,
        MigrationRootStanding newRoot,
        EncodeUnaskedStanding whereEncodesGo,
        MigrationCarryStanding carrying)
        => Every(newRoot, whereEncodesGo, carrying).Single(standing => standing.Subject == subject);
}
