using Carina.Domain.Captions;
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
    public void BrKd025TheDirectoryCaptionsAreKeptInIsWalkedBesideTheOthers()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();
        using var pictures = new TempTree();
        using var captions = new TempTree();

        LocalWrittenFileSurvey survey = Survey(recordings.Root, encodes.Root, pictures.Root, captions.Root);

        Assert.Equal(["encodes", "thumbnails", "captions"], survey.Places.Select(place => place.Value).ToArray());
    }

    [Fact]
    public async Task BrKd025AWalkOfTheCaptionsSaysItListedTheCaptions()
    {
        using var recordings = new TempTree();
        using var captions = new TempTree();
        captions.Holding("one.captions", 9);

        RootListing listed = await Survey(recordings.Root, null, null, captions.Root).ListAsync(LocalWrittenFileSurvey.CaptionPlace, Cancel);

        Assert.Equal(StoragePlace.Captions, listed.Place);
        Assert.Equal(["one.captions"], listed.Files.Select(file => file.Path).ToArray());
    }

    [Fact]
    public void BrKd025EveryRecordingTheLedgerHoldsClaimsItsCaptionsAndNothingElseOnTheirShelf()
    {
        using var recordings = new TempTree();
        using var captions = new TempTree();

        DeclaredFile claimed = Assert.Single(Survey(recordings.Root, null, null, captions.Root).Claimed([Row], []));

        Assert.Equal(new DeclaredFile(LocalWrittenFileSurvey.CaptionPlace, Recorded.Wire + ".captions"), claimed);
        Assert.Empty(Survey(recordings.Root, null, null, captions.Root).Drawn([Row]));
    }

    [Fact]
    public void BrKd025CaptionsKeptInsideTheRecordingRootAreNotWalkedAndAreClaimedThere()
    {
        using var recordings = new TempTree();
        recordings.HoldingDirectory("captions");

        LocalWrittenFileSurvey survey = Survey(recordings.Root, null, null, recordings.Under("captions"));

        Assert.DoesNotContain(LocalWrittenFileSurvey.CaptionPlace, survey.Places);
        Assert.Contains(new DeclaredFile(Primary, "captions/" + Recorded.Wire + ".captions"), survey.Claimed([Row], []));
    }

    [Fact]
    public void CaptionsKeptInTheDirectoryThumbnailsAreDrawnIntoAreClaimedThere()
    {
        using var recordings = new TempTree();
        using var shared = new TempTree();

        LocalWrittenFileSurvey survey = Survey(recordings.Root, null, shared.Root, shared.Root);

        Assert.Equal(["thumbnails"], survey.Places.Select(place => place.Value).ToArray());
        Assert.Contains(new DeclaredFile(LocalWrittenFileSurvey.ThumbnailPlace, Recorded.Wire + ".captions"), survey.Claimed([Row], []));
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
            new CaptionSettings(),
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

    private static readonly RecordingId Recorded = new(new Guid("7a3e5c1b-0000-0000-0000-000000000001"));

    private static readonly LedgerFile Row =
        LedgerFile.StillWriting(Recorded, Primary, new RecordingFileName(Recorded.Wire + ".ts"));

    private static readonly string Picture = Recorded.Wire + ".jpg";

    [Fact]
    public void EveryRecordingTheLedgerHoldsClaimsItsPicture()
    {
        using var recordings = new TempTree();
        using var pictures = new TempTree();

        DeclaredFile claimed = Assert.Single(Survey(recordings.Root, null, pictures.Root).Claimed([Row], []));

        Assert.Equal(new DeclaredFile(LocalWrittenFileSurvey.ThumbnailPlace, Picture), claimed);
        Assert.Empty(Survey(recordings.Root, null, null).Claimed([Row], []));
    }

    [Fact]
    public void EveryRecordingWhoseRowSaysItsPictureIsDrawnIsLookedForInTheThumbnailPlace()
    {
        using var recordings = new TempTree();
        using var pictures = new TempTree();
        LedgerFile drawn = LedgerFile.Ended(
            Recorded,
            Primary,
            new RecordingFileName(Recorded.Wire + ".ts"),
            LedgerClaim.EverythingLanded,
            100,
            thumbnailDrawn: true);

        DrawnPicture picture = Assert.Single(Survey(recordings.Root, null, pictures.Root).Drawn([drawn, Row]));

        Assert.Equal(Recorded, picture.Recording);
        Assert.Equal(LocalWrittenFileSurvey.ThumbnailPlace, picture.Place);
        Assert.Equal(Picture, picture.Path);
        Assert.Empty(Survey(recordings.Root, null, null).Drawn([drawn]));
    }

    [Fact]
    public void PicturesDrawnIntoTheEncodeRootAreClaimedThereEvenThoughTheirOwnPlaceIsNotWalked()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();
        LocalWrittenFileSurvey survey = Survey(recordings.Root, encodes.Root, encodes.Root);

        IReadOnlyList<DeclaredFile> claimed = survey.Claimed([Row], []);

        Assert.DoesNotContain(LocalWrittenFileSurvey.ThumbnailPlace, survey.Places);
        Assert.Contains(new DeclaredFile(Encodes, Picture), claimed);
    }

    [Fact]
    public void PicturesDrawnIntoADirectoryInsideTheEncodeRootAreClaimedByTheirPathUnderIt()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();
        encodes.HoldingDirectory("thumbs");

        IReadOnlyList<DeclaredFile> claimed =
            Survey(recordings.Root, encodes.Root, encodes.Under("thumbs")).Claimed([Row], []);

        Assert.Contains(new DeclaredFile(Encodes, "thumbs/" + Picture), claimed);
    }

    [Fact]
    public void PicturesDrawnInsideTheRecordingRootAreClaimedThereToo()
    {
        using var recordings = new TempTree();
        recordings.HoldingDirectory("thumbs");

        IReadOnlyList<DeclaredFile> claimed =
            Survey(recordings.Root, null, recordings.Under("thumbs")).Claimed([Row], []);

        Assert.Contains(new DeclaredFile(Primary, "thumbs/" + Picture), claimed);
    }

    [Fact]
    public void AnArtefactClaimedInAnEncodeRootSetOnTheRecordingRootIsClaimedUnderTheRecordingRoot()
    {
        using var recordings = new TempTree();

        IReadOnlyList<DeclaredFile> claimed = Survey(recordings.Root, recordings.Root, null)
            .Claimed([], [new DeclaredFile(Encodes, "one.mp4")]);

        Assert.Contains(new DeclaredFile(Primary, "one.mp4"), claimed);
        Assert.Contains(new DeclaredFile(Encodes, "one.mp4"), claimed);
    }

    [Fact]
    public void AClaimOutsideEveryWalkedPlaceIsHandedBackAsItCame()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();

        Assert.Equal(
            [new DeclaredFile(Encodes, "one.mp4")],
            Survey(recordings.Root, encodes.Root, null).Claimed([], [new DeclaredFile(Encodes, "one.mp4")]));
    }

    private static LocalWrittenFileSurvey Survey(string recordings, string? encodes, string? pictures, string? captions = null)
        => new(
            new IntegritySettings { OutputRoots = [new StorageRootPath(Primary, recordings)] },
            new EncodeSettings
            {
                OutputRoots = encodes is null ? [] : [new StorageRootPath(Encodes, encodes)],
            },
            new ThumbnailSettings { WrittenTo = pictures },
            new CaptionSettings { WrittenTo = captions },
            NullLogger<LocalWrittenFileSurvey>.Instance);
}
