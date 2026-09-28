using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Integrity;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Integrity;

public sealed class LocalWrittenFileSurveyTests
{
    private static readonly OutputRoot Primary = new("primary");

    private static readonly OutputRoot Encodes = new("encodes");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public void TheRootsThisProcessEncodesIntoAndTheThumbnailsAreTheOnesItWalks()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();
        using var pictures = new TempTree();

        LocalWrittenFileSurvey survey = Survey(recordings.Root, encodes.Root, pictures.Root);

        Assert.Equal(["encodes", "thumbnails"], survey.Places.Select(place => place.Value).ToArray());
    }

    [Fact]
    public void NothingIsWalkedWhenThisProcessWritesNowhere()
    {
        using var recordings = new TempTree();

        Assert.Empty(Survey(recordings.Root, null, null).Places);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/inside")]
    public void AnEncodeRootOnTheRecordingRootOrInsideItIsNotWalked(string below)
    {
        using var recordings = new TempTree();
        recordings.HoldingDirectory("inside");

        LocalWrittenFileSurvey survey = Survey(recordings.Root, recordings.Root + below, null);

        Assert.Empty(survey.Places);
    }

    [Fact]
    public void AnEncodeRootAroundTheRecordingRootIsNotWalked()
    {
        using var outer = new TempTree();
        outer.HoldingDirectory("recordings");

        Assert.Empty(Survey(outer.Under("recordings"), outer.Root, null).Places);
    }

    [Fact]
    public void AnEncodeRootNamedLikeARecordingRootIsNotWalked()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();

        LocalWrittenFileSurvey survey = new(
            new IntegritySettings { OutputRoots = [new StorageRootPath(Primary, recordings.Root)] },
            new EncodeSettings { OutputRoots = [new StorageRootPath(Primary, encodes.Root)] },
            new ThumbnailSettings(),
            NullLogger<LocalWrittenFileSurvey>.Instance);

        Assert.Empty(survey.Places);
    }

    [Fact]
    public void ThumbnailsDrawnIntoAnEncodeRootAreNotWalkedTwice()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();

        Assert.Equal(
            ["encodes"],
            Survey(recordings.Root, encodes.Root, encodes.Root).Places.Select(place => place.Value).ToArray());
    }

    [Fact]
    public async Task AWalkSaysWhichKindOfPlaceItListed()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();
        using var pictures = new TempTree();
        encodes.Holding("one.mp4", 5);
        pictures.Holding("one.jpg", 3);
        LocalWrittenFileSurvey survey = Survey(recordings.Root, encodes.Root, pictures.Root);

        RootListing artefacts = await survey.ListAsync(Encodes, Cancel);
        RootListing drawn = await survey.ListAsync(LocalWrittenFileSurvey.ThumbnailPlace, Cancel);

        Assert.Equal(StoragePlace.Encodes, artefacts.Place);
        Assert.Equal(5, artefacts.At("one.mp4")?.SizeBytes);
        Assert.Equal(StoragePlace.Thumbnails, drawn.Place);
        Assert.Equal(3, drawn.At("one.jpg")?.SizeBytes);
    }

    [Fact]
    public async Task APlaceNotThereYetIsOutOfReach()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();

        RootListing listing = await Survey(recordings.Root, encodes.Under("not-yet"), null).ListAsync(Encodes, Cancel);

        Assert.False(listing.Reachable);
        Assert.Equal(StoragePlace.Encodes, listing.Place);
    }

    [Fact]
    public async Task APlaceItDoesNotWalkIsRefused()
    {
        using var recordings = new TempTree();

        await Assert.ThrowsAsync<ArgumentException>(
            () => Survey(recordings.Root, null, null).ListAsync(Primary, Cancel));
    }

    [Fact]
    public void EveryRecordingTheLedgerHoldsClaimsItsPicture()
    {
        using var recordings = new TempTree();
        using var pictures = new TempTree();
        var id = new RecordingId(new Guid("7a3e5c1b-0000-0000-0000-000000000001"));
        LedgerFile row = LedgerFile.StillWriting(id, Primary, new RecordingFileName(id.Wire + ".ts"));

        DeclaredFile claimed = Assert.Single(Survey(recordings.Root, null, pictures.Root).PicturesOf([row]));

        Assert.Equal(LocalWrittenFileSurvey.ThumbnailPlace, claimed.Root);
        Assert.Equal(id.Wire + ".jpg", claimed.Path);
        Assert.Empty(Survey(recordings.Root, null, null).PicturesOf([row]));
    }

    private static LocalWrittenFileSurvey Survey(string recordings, string? encodes, string? pictures)
        => new(
            new IntegritySettings { OutputRoots = [new StorageRootPath(Primary, recordings)] },
            new EncodeSettings
            {
                OutputRoots = encodes is null ? [] : [new StorageRootPath(Encodes, encodes)],
            },
            new ThumbnailSettings { WrittenTo = pictures },
            NullLogger<LocalWrittenFileSurvey>.Instance);
}
