using Carina.Domain.Encodings;
using Carina.Domain.Migration;
using Carina.Domain.Recordings;

using static Carina.Domain.Tests.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationPreconditionsTests
{
    private static readonly EncodeProfile Profile = EncodeProfile.Define(
        EncodeProfileId.New(),
        new EncodeLabel("Viewing"),
        EncodeCodec.H264,
        EncodeResolution.AsSource,
        Deinterlace.EveryFrame,
        new ConstantRateFactor(22),
        new ConstantQuantiser(24),
        Began);

    private static readonly EncodeDestination Destination = EncodeDestination.Define(
        EncodeDestinationId.New(),
        new EncodeLabel("Encodes"),
        new OutputRoot("encodes"),
        Profile.Id,
        Began);

    private static readonly EncodeUnasked Settled = EncodeUnasked.Of([Destination], [Profile]);

    [Fact(DisplayName = "a run for real into an empty new root, with where encodes go settled and a hard link to be made, is not stopped")]
    public void ARunForRealWithEverythingInPlaceIsNotStopped()
        => Assert.Null(Found().Hindering(MigrationPass.ForReal));

    [Theory(DisplayName = "BR-MS-002: a run for real never adds to a new root that is not empty, or carries into one that is not there")]
    [InlineData(MigrationRootStanding.NotEmpty)]
    [InlineData(MigrationRootStanding.Missing)]
    public void ARunForRealStartsOnlyIntoAnEmptyNewRoot(MigrationRootStanding standing)
        => Assert.Equal(
            MigrationHindrance.TheNewRootIsNotEmptyOrNotThere,
            Found(newRoot: standing).Hindering(MigrationPass.ForReal));

    [Fact(DisplayName = "a run for real does not start while nothing defines where what it carries is encoded")]
    public void ARunForRealDoesNotStartWithNowhereToEncode()
        => Assert.Equal(
            MigrationHindrance.NothingSettlesWhereEncodesGo,
            Found(whereEncodesGo: EncodeUnasked.Of([], [Profile])).Hindering(MigrationPass.ForReal));

    [Fact(DisplayName = "a run for real does not guess between two destinations for what it carries")]
    public void ARunForRealDoesNotGuessBetweenTwoDestinations()
    {
        EncodeDestination another = EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel("Elsewhere"),
            new OutputRoot("elsewhere"),
            Profile.Id,
            Began);

        Assert.Equal(
            MigrationHindrance.NothingSettlesWhereEncodesGo,
            Found(whereEncodesGo: EncodeUnasked.Of([Destination, another], [Profile])).Hindering(MigrationPass.ForReal));
    }

    [Fact(DisplayName = "a run for real does not start while the destination's default profile is not offered")]
    public void ARunForRealDoesNotStartWithoutTheDefaultProfile()
        => Assert.Equal(
            MigrationHindrance.NothingSettlesWhereEncodesGo,
            Found(whereEncodesGo: EncodeUnasked.Of([Destination], [])).Hindering(MigrationPass.ForReal));

    [Theory(DisplayName = "BR-MD-001: a run for real stops before the first link when the link would not be a hard link")]
    [InlineData(MigrationCarryStanding.WouldCrossAMount)]
    [InlineData(MigrationCarryStanding.TheNewRootDoesNotTakeALink)]
    public void ARunForRealStopsWhenNoLinkCanBeMade(MigrationCarryStanding carrying)
        => Assert.Equal(
            MigrationHindrance.NoLinkCanBeMade,
            Found(carrying: carrying).Hindering(MigrationPass.ForReal));

    [Fact(DisplayName = "a run for real is not stopped by a source that has nothing left to carry")]
    public void ARunForRealIsNotStoppedByASourceWithNothingToCarry()
        => Assert.Null(Found(carrying: MigrationCarryStanding.NothingIsThereToCarry).Hindering(MigrationPass.ForReal));

    [Fact(DisplayName = "the new root is asked about first, then where encodes go, then the link")]
    public void TheNewRootIsAskedAboutFirst()
    {
        MigrationPreconditions everythingWrong = Found(
            MigrationRootStanding.NotEmpty,
            EncodeUnasked.Of([], []),
            MigrationCarryStanding.WouldCrossAMount);
        MigrationPreconditions encodesAndLink = Found(
            whereEncodesGo: EncodeUnasked.Of([], []),
            carrying: MigrationCarryStanding.WouldCrossAMount);

        Assert.Equal(MigrationHindrance.TheNewRootIsNotEmptyOrNotThere, everythingWrong.Hindering(MigrationPass.ForReal));
        Assert.Equal(MigrationHindrance.NothingSettlesWhereEncodesGo, encodesAndLink.Hindering(MigrationPass.ForReal));
    }

    [Fact(DisplayName = "BR-MD-005: a rehearsal is never stopped, so it can say what a run for real would meet")]
    public void ARehearsalIsNeverStopped()
        => Assert.Null(Found(
                MigrationRootStanding.NotEmpty,
                EncodeUnasked.Of([], []),
                MigrationCarryStanding.WouldCrossAMount)
            .Hindering(MigrationPass.Rehearsal));

    private static MigrationPreconditions Found(
        MigrationRootStanding newRoot = MigrationRootStanding.Empty,
        EncodeUnasked? whereEncodesGo = null,
        MigrationCarryStanding carrying = MigrationCarryStanding.WouldBeAHardLink)
        => new(newRoot, whereEncodesGo ?? Settled, carrying);
}
