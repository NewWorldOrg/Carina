using Carina.Domain.Encodings;
using Carina.Domain.Migration;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationStandingTests
{
    private static readonly MigrationRunId Run = new(new Guid("00000001-0000-0000-0000-000000000001"));

    [Fact]
    public void ARunSaysWhatItFoundAboutEverySubjectAndSaysItOnce()
    {
        IReadOnlyList<MigrationStanding> stood = MigrationStanding.EveryOne(
            Run,
            MigrationRootStanding.Empty,
            EncodeUnaskedStanding.Settled);

        Assert.Equal(MigrationStandingSubjects.All, stood.Select(standing => standing.Subject).Order());
    }

    [Theory]
    [InlineData(MigrationRootStanding.Empty, MigrationFinding.TheNewRootIsEmpty)]
    [InlineData(MigrationRootStanding.NotEmpty, MigrationFinding.TheNewRootIsNotEmpty)]
    [InlineData(MigrationRootStanding.Missing, MigrationFinding.TheNewRootIsNotThere)]
    public void HowTheNewRootStandsIsWrittenDownWhicheverWayItStands(
        MigrationRootStanding standing,
        MigrationFinding written)
        => Assert.Equal(
            written,
            Found(MigrationStandingSubject.TheNewRoot, standing, EncodeUnaskedStanding.Settled));

    [Theory]
    [InlineData(EncodeUnaskedStanding.Settled, MigrationFinding.WhereEncodesGoIsSettled)]
    [InlineData(EncodeUnaskedStanding.NothingIsDefined, MigrationFinding.NothingSaysWhereEncodesGo)]
    [InlineData(EncodeUnaskedStanding.MoreThanOneIsOffered, MigrationFinding.MoreThanOneSaysWhereEncodesGo)]
    [InlineData(EncodeUnaskedStanding.TheProfileIsNotOffered, MigrationFinding.TheProfileIsNotOffered)]
    public void WhereEncodesGoIsWrittenDownWhicheverWayItStands(
        EncodeUnaskedStanding standing,
        MigrationFinding written)
        => Assert.Equal(
            written,
            Found(MigrationStandingSubject.WhereEncodesGo, MigrationRootStanding.Empty, standing));

    [Fact]
    public void NothingInTheWayIsTheOnlyPairARunForRealWouldGetPast()
    {
        Assert.All(
            MigrationStanding.EveryOne(Run, MigrationRootStanding.Empty, EncodeUnaskedStanding.Settled),
            standing => Assert.False(standing.WouldStopARunForReal));

        Assert.All(
            MigrationStanding.EveryOne(
                Run,
                MigrationRootStanding.NotEmpty,
                EncodeUnaskedStanding.MoreThanOneIsOffered),
            standing => Assert.True(standing.WouldStopARunForReal));
    }

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
            [MigrationFinding.TheNewRootIsEmpty, MigrationFinding.WhereEncodesGoIsSettled],
            MigrationFindings.All.Where(finding => !MigrationFindings.WouldStopARunForReal(finding)).Order());
    }

    [Fact]
    public void ASubjectOutsideTheOnesTheRecordKnowsIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MigrationStandingSubjects.Named((MigrationStandingSubject)9));
        Assert.Throws<ArgumentOutOfRangeException>(() => MigrationFindings.Named((MigrationFinding)9));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MigrationFindings.Of((MigrationRootStanding)9));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MigrationFindings.Of((EncodeUnaskedStanding)9));
    }

    private static MigrationFinding Found(
        MigrationStandingSubject subject,
        MigrationRootStanding newRoot,
        EncodeUnaskedStanding whereEncodesGo)
        => MigrationStanding.EveryOne(Run, newRoot, whereEncodesGo)
            .Single(standing => standing.Subject == subject)
            .Finding;
}
