using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeUnaskedTests
{
    private static readonly DateTime At = new(2026, 9, 4, 3, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-ED2-004: a machine with one destination still offered needs nobody to say where an artefact goes")]
    public void AMachineWithOneDestinationStillOfferedNeedsNobodyToSayWhereAnArtefactGoes()
    {
        EncodeProfile profile = Profile();
        EncodeDestination shelf = Shelf(profile.Id);

        EncodeUnasked unasked = EncodeUnasked.Of([shelf], [profile]);

        Assert.True(unasked.IsSettled);
        Assert.Equal(EncodeUnaskedStanding.Settled, unasked.Standing);
        Assert.Same(shelf, unasked.Destination);
        Assert.Same(profile, unasked.Profile);
    }

    [Fact(DisplayName = "BR-ED2-004: a retired destination is not one this machine can settle on")]
    public void ARetiredDestinationIsNotOneThisMachineCanSettleOn()
    {
        EncodeProfile profile = Profile();
        EncodeDestination shelf = Shelf(profile.Id);
        EncodeDestination gone = Shelf(profile.Id);
        gone.Retire(At.AddDays(1));

        EncodeUnasked unasked = EncodeUnasked.Of([gone, shelf], [profile]);

        Assert.True(unasked.IsSettled);
        Assert.Same(shelf, unasked.Destination);
    }

    [Fact(DisplayName = "BR-ED2-004: a machine with no destination at all settles nothing and says which is missing")]
    public void AMachineWithNoDestinationAtAllSettlesNothing()
    {
        EncodeUnasked unasked = EncodeUnasked.Of([], [Profile()]);

        Assert.False(unasked.IsSettled);
        Assert.Equal(EncodeUnaskedStanding.NothingIsDefined, unasked.Standing);
        Assert.Null(unasked.Destination);
        Assert.Null(unasked.Profile);
    }

    [Fact(DisplayName = "BR-ED2-004: a machine offering more than one destination makes nobody's choice for them")]
    public void AMachineOfferingMoreThanOneDestinationMakesNobodysChoice()
    {
        EncodeProfile profile = Profile();

        EncodeUnasked unasked = EncodeUnasked.Of([Shelf(profile.Id), Shelf(profile.Id)], [profile]);

        Assert.False(unasked.IsSettled);
        Assert.Equal(EncodeUnaskedStanding.MoreThanOneIsOffered, unasked.Standing);
        Assert.Null(unasked.Destination);
    }

    [Fact(DisplayName = "BR-ED2-004: a destination whose profile is no longer offered settles nothing")]
    public void ADestinationWhoseProfileIsNoLongerOfferedSettlesNothing()
    {
        EncodeProfile profile = Profile();
        profile.Retire(At.AddDays(1));

        EncodeUnasked unasked = EncodeUnasked.Of([Shelf(profile.Id)], [profile]);

        Assert.False(unasked.IsSettled);
        Assert.Equal(EncodeUnaskedStanding.TheProfileIsNotOffered, unasked.Standing);
    }

    [Fact(DisplayName = "BR-ED2-004: a destination naming a profile this machine has never heard of settles nothing")]
    public void ADestinationNamingAProfileThisMachineHasNeverHeardOfSettlesNothing()
    {
        EncodeUnasked unasked = EncodeUnasked.Of([Shelf(EncodeProfileId.New())], [Profile()]);

        Assert.False(unasked.IsSettled);
        Assert.Equal(EncodeUnaskedStanding.TheProfileIsNotOffered, unasked.Standing);
    }

    private static EncodeProfile Profile()
        => EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Standard"),
            EncodeCodec.H264,
            EncodeResolution.AsSource,
            Deinterlace.Leave,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            At);

    private static EncodeDestination Shelf(EncodeProfileId profileId)
        => EncodeDestination.Define(
            new EncodeDestinationId(Guid.NewGuid()),
            new EncodeLabel("Shelf"),
            new OutputRoot("encodes"),
            profileId,
            At);
}
