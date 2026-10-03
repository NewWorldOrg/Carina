using Carina.Domain.Captions;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Integrity;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Integrity;

public sealed class LocalStrayFileEraserTests
{
    private static readonly OutputRoot Primary = new("primary");

    private static readonly OutputRoot Encodes = new("encodes");

    private static readonly DateTime Noticed = new(2026, 9, 28, 3, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task AFileNothingClaimsIsTakenOffTheDiskAndNothingBesideItIsTouched()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();
        encodes.Holding("left.mp4", 40).Holding("kept.mp4", 10);
        recordings.Holding("one.ts", 10);
        IReadOnlyList<string> recordingsBefore = recordings.Snapshot();

        StrayFileErasure erased = await Eraser(recordings, encodes).EraseAsync(Found(encodes, "left.mp4"), Cancel);

        Assert.Null(erased.Fault);
        Assert.True(erased.FileRemoved);
        Assert.False(File.Exists(encodes.Under("left.mp4")));
        Assert.True(File.Exists(encodes.Under("kept.mp4")));
        Assert.Equal(recordingsBefore, recordings.Snapshot());
    }

    [Fact]
    public async Task AFileWrittenToSinceItWasFoundIsLeftAsItNowIs()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();
        encodes.Holding("left.mp4", 40);
        IntegrityFinding finding = Found(encodes, "left.mp4");
        await File.AppendAllTextAsync(encodes.Under("left.mp4"), "more");

        StrayFileErasure refused = await Eraser(recordings, encodes).EraseAsync(finding, Cancel);

        Assert.Equal(StrayErasureFault.FileChanged, refused.Fault);
        Assert.Equal(StrayFileChange.Resized, refused.Change);
        Assert.Equal(44, new FileInfo(encodes.Under("left.mp4")).Length);
    }

    [Fact]
    public async Task AFileRewrittenAtTheSameSizeIsLeftWhereItIs()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();
        encodes.Holding("left.mp4", 40).Holding("kept.mp4", 1);
        IntegrityFinding finding = Found(encodes, "left.mp4");
        File.SetLastWriteTimeUtc(encodes.Under("left.mp4"), finding.LastWrittenAt!.Value.AddMinutes(1));

        StrayFileErasure refused = await Eraser(recordings, encodes).EraseAsync(finding, Cancel);

        Assert.Equal(StrayFileChange.Rewritten, refused.Change);
        Assert.True(File.Exists(encodes.Under("left.mp4")));
    }

    [Fact]
    public async Task AFileAlreadyGoneIsSaidToBeGone()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();
        encodes.Holding("left.mp4", 40).Holding("kept.mp4", 1);
        IntegrityFinding finding = Found(encodes, "left.mp4");
        File.Delete(encodes.Under("left.mp4"));

        StrayFileErasure refused = await Eraser(recordings, encodes).EraseAsync(finding, Cancel);

        Assert.Equal(StrayFileChange.Gone, refused.Change);
    }

    [Fact]
    public async Task AFileReachedThroughALinkIsNotRemovedNorIsWhatTheLinkPointsAt()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();
        recordings.Holding("one.ts", 40);
        encodes.Holding("kept.mp4", 1);
        File.CreateSymbolicLink(encodes.Under("one.ts"), recordings.Under("one.ts"));
        IntegrityFinding finding = IntegrityFinding.NoLedgerRow(
            IntegrityCheckId.New(),
            Encodes,
            "one.ts",
            40,
            Noticed,
            new FileInfo(recordings.Under("one.ts")).LastWriteTimeUtc);

        StrayFileErasure refused = await Eraser(recordings, encodes).EraseAsync(finding, Cancel);

        Assert.Equal(StrayErasureFault.FileLeftBehind, refused.Fault);
        Assert.True(File.Exists(recordings.Under("one.ts")));
    }

    [Fact]
    public async Task APlaceThisProcessDoesNotWriteIntoIsNeverTouched()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();
        recordings.Holding("left.ts", 40);
        IntegrityFinding finding = IntegrityFinding.NoLedgerRow(
            IntegrityCheckId.New(),
            Primary,
            "left.ts",
            40,
            Noticed,
            new FileInfo(recordings.Under("left.ts")).LastWriteTimeUtc);

        StrayFileErasure refused = await Eraser(recordings, encodes).EraseAsync(finding, Cancel);

        Assert.Equal(StrayErasureFault.RootOutOfReach, refused.Fault);
        Assert.True(File.Exists(recordings.Under("left.ts")));
    }

    [Theory]
    [InlineData("../outside.mp4")]
    [InlineData("nested/../../outside.mp4")]
    [InlineData("/etc/passwd")]
    public void APathThatLeavesThePlaceIsNotPlacedAnywhere(string path)
        => Assert.Null(LocalStrayFileEraser.PlaceUnder("/srv/encodes", path));

    [Fact]
    public async Task ThePlacedEraserRemovesFromItsOwnPlacesItselfAndAsksTheDriverForTheRecordingRoots()
    {
        using var recordings = new TempTree();
        using var encodes = new TempTree();
        recordings.Holding("left.tmp", 40);
        encodes.Holding("left.mp4", 40);
        var driver = new ErasingDriverClient();
        LocalWrittenFileSurvey places = Survey(recordings, encodes);
        var placed = new PlacedStrayFileEraser(
            places,
            new LocalStrayFileEraser(places, NullLogger<LocalStrayFileEraser>.Instance),
            new DriverStrayFileEraser(
                driver,
                new LocalRecordingFileSurvey(
                    new IntegritySettings { OutputRoots = [new StorageRootPath(Primary, recordings.Root)] },
                    NullLogger<LocalRecordingFileSurvey>.Instance),
                NullLogger<DriverStrayFileEraser>.Instance));
        FileInfo tmp = new(recordings.Under("left.tmp"));

        await placed.EraseAsync(Found(encodes, "left.mp4"), Cancel);

        Assert.False(File.Exists(encodes.Under("left.mp4")));
        Assert.Empty(driver.AskedAboutStrays);

        await placed.EraseAsync(
            IntegrityFinding.NoLedgerRow(IntegrityCheckId.New(), Primary, "left.tmp", 40, Noticed, tmp.LastWriteTimeUtc),
            Cancel);

        Assert.Equal("left.tmp", Assert.Single(driver.AskedAboutStrays).Path);
        Assert.True(File.Exists(recordings.Under("left.tmp")));
    }

    private static LocalStrayFileEraser Eraser(TempTree recordings, TempTree encodes)
        => new(Survey(recordings, encodes), NullLogger<LocalStrayFileEraser>.Instance);

    private static LocalWrittenFileSurvey Survey(TempTree recordings, TempTree encodes)
        => new(
            new IntegritySettings { OutputRoots = [new StorageRootPath(Primary, recordings.Root)] },
            new EncodeSettings { OutputRoots = [new StorageRootPath(Encodes, encodes.Root)] },
            new ThumbnailSettings(),
            new CaptionSettings(),
            NullLogger<LocalWrittenFileSurvey>.Instance);

    private static IntegrityFinding Found(TempTree encodes, string path)
    {
        FileInfo file = new(encodes.Under(path));

        return IntegrityFinding.NoLedgerRow(
            IntegrityCheckId.New(),
            Encodes,
            path,
            file.Length,
            Noticed,
            file.LastWriteTimeUtc);
    }
}
