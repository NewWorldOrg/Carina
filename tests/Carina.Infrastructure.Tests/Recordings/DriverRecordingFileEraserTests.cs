using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Recordings;
using Carina.Infrastructure.Thumbnails;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class DriverRecordingFileEraserTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly OutputRoot Primary = new("primary");

    private readonly ErasingDriverClient driver = new();

    private readonly string gallery = Directory.CreateTempSubdirectory("carina-erase-pictures-").FullName;

    public void Dispose() => Directory.Delete(gallery, recursive: true);

    [Fact]
    public async Task TheAppNeverRemovesTheRecordingItselfAndAsksTheProcessThatOwnsTheRootInstead()
    {
        RecordingId id = RecordingId.New();

        RecordingErasure erased = await Eraser().EraseAsync(id, Primary, Cancel);

        Assert.True(erased.EverythingIsGone);
        Assert.Equal([(id.Wire, "primary")], driver.Asked);
    }

    [Fact]
    public async Task TheRecordingAndItsPictureAreBothCountedWhenBothWereThere()
    {
        RecordingId id = RecordingId.New();
        string drawn = Drawn(id);

        RecordingErasure erased = await Eraser().EraseAsync(id, Primary, Cancel);

        Assert.True(erased.EverythingIsGone);
        Assert.Equal(2, erased.FilesRemoved);
        Assert.False(File.Exists(drawn));
    }

    [Fact]
    public async Task ARecordingWithNoPictureDrawnYetIsStillThrownAway()
    {
        RecordingErasure erased = await Eraser().EraseAsync(RecordingId.New(), Primary, Cancel);

        Assert.True(erased.EverythingIsGone);
        Assert.Equal(1, erased.FilesRemoved);
    }

    [Fact]
    public async Task AFileTheOwningProcessSaysWasAlreadyGoneIsNotCounted()
    {
        driver.Answer = DriverCall<RecordingErasedDto>.Reached(
            new RecordingErasedDto { FileRemoved = false });

        RecordingErasure erased = await Eraser().EraseAsync(RecordingId.New(), Primary, Cancel);

        Assert.True(erased.EverythingIsGone);
        Assert.Equal(0, erased.FilesRemoved);
    }

    [Fact]
    public async Task NothingBesideTheRecordingAskedForIsTouched()
    {
        RecordingId asked = RecordingId.New();
        RecordingId beside = RecordingId.New();
        string neighbour = Drawn(beside);
        Drawn(asked);

        await Eraser().EraseAsync(asked, Primary, Cancel);

        Assert.True(File.Exists(neighbour));
    }

    [Fact]
    public async Task WhereNoDirectoryIsConfiguredForPicturesTheRecordingStillGoes()
    {
        RecordingId id = RecordingId.New();
        string drawn = Drawn(id);

        RecordingErasure erased = await EraserWithNowhereForPictures().EraseAsync(id, Primary, Cancel);

        Assert.True(erased.EverythingIsGone);
        Assert.Equal(1, erased.FilesRemoved);
        Assert.True(File.Exists(drawn));
    }

    [Fact]
    public async Task APictureThatWillNotComeOffTheDiskIsReportedEvenThoughTheRecordingWent()
    {
        RecordingId id = RecordingId.New();
        Directory.CreateDirectory(Path.Combine(gallery, id.Wire + ThumbnailJob.Extension));

        RecordingErasure refused = await Eraser().EraseAsync(id, Primary, Cancel);

        Assert.Equal(ErasureFault.FileLeftBehind, refused.Fault);
        Assert.Equal(0, refused.FilesRemoved);
    }

    [Fact]
    public async Task ADriverThatCannotBeReachedLeavesThePictureWhereItIs()
    {
        RecordingId id = RecordingId.New();
        string drawn = Drawn(id);
        driver.Answer = DriverCall<RecordingErasedDto>.Unreachable("the socket was not there");

        RecordingErasure refused = await Eraser().EraseAsync(id, Primary, Cancel);

        Assert.Equal(ErasureFault.DriverUnreachable, refused.Fault);
        Assert.Equal("the socket was not there", refused.Note);
        Assert.True(File.Exists(drawn));
    }

    [Fact]
    public async Task ADriverTooOldToThrowARecordingAwayLeavesThePictureWhereItIs()
    {
        RecordingId id = RecordingId.New();
        string drawn = Drawn(id);
        driver.Answer = DriverCall<RecordingErasedDto>.Refused(
            new DriverProblem(SessionRefusalTitles.CapabilityMissing, ["it declares no such thing"]));

        RecordingErasure refused = await Eraser().EraseAsync(id, Primary, Cancel);

        Assert.Equal(ErasureFault.DriverRefused, refused.Fault);
        Assert.Contains("it declares no such thing", refused.Note!, StringComparison.Ordinal);
        Assert.True(File.Exists(drawn));
    }

    [Fact]
    public async Task ARootTheOwningProcessCannotReachIsStillARootOutOfReachHere()
    {
        RecordingId id = RecordingId.New();
        string drawn = Drawn(id);
        driver.Answer = DriverCall<RecordingErasedDto>.Refused(
            new DriverProblem(SessionRefusalTitles.OutputUnavailable, ["the mount has gone"]));

        RecordingErasure refused = await Eraser().EraseAsync(id, Primary, Cancel);

        Assert.Equal(ErasureFault.RootOutOfReach, refused.Fault);
        Assert.True(File.Exists(drawn));
    }

    [Fact]
    public async Task AFileTheOwningProcessCouldNotRemoveIsStillAFileLeftBehindHere()
    {
        RecordingId id = RecordingId.New();
        string drawn = Drawn(id);
        driver.Answer = DriverCall<RecordingErasedDto>.Refused(
            new DriverProblem(SessionRefusalTitles.FileLeftBehind, ["permission denied"]));

        RecordingErasure refused = await Eraser().EraseAsync(id, Primary, Cancel);

        Assert.Equal(ErasureFault.FileLeftBehind, refused.Fault);
        Assert.True(File.Exists(drawn));
    }

    [Fact]
    public void TheNameTheLedgerHoldsIsTheNameTheOwningProcessDerivesForItself()
    {
        RecordingId id = RecordingId.New();

        Assert.Equal(
            RecordingFile.Of(id.Wire),
            RecordingFileName.For(id, RecordingSettings.FileExtension).Value);
    }

    private string Drawn(RecordingId id)
    {
        string path = Path.Combine(gallery, id.Wire + ThumbnailJob.Extension);
        File.WriteAllBytes(path, new byte[16]);

        return path;
    }

    private DriverRecordingFileEraser Eraser() => Built(gallery);

    private DriverRecordingFileEraser EraserWithNowhereForPictures() => Built(null);

    private DriverRecordingFileEraser Built(string? pictures)
        => new(
            driver,
            new ThumbnailSettings { WrittenTo = pictures },
            NullLogger<DriverRecordingFileEraser>.Instance);
}
