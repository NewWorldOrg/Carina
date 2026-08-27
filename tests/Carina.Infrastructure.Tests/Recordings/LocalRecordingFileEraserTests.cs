using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Recordings;
using Carina.Infrastructure.Thumbnails;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class LocalRecordingFileEraserTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly OutputRoot Primary = new("primary");

    private readonly string root = Directory.CreateTempSubdirectory("carina-erase-").FullName;

    private readonly string gallery = Directory.CreateTempSubdirectory("carina-erase-pictures-").FullName;

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
        Directory.Delete(gallery, recursive: true);
    }

    [Fact]
    public async Task TheRecordingAndItsPictureBothLeaveTheDisk()
    {
        RecordingId id = RecordingId.New();
        RecordingFileName name = Holding(id);
        string drawn = Drawn(id);

        RecordingErasure erased = await Eraser().EraseAsync(id, Primary, name, Cancel);

        Assert.True(erased.EverythingIsGone);
        Assert.Equal(2, erased.FilesRemoved);
        Assert.False(File.Exists(Path.Combine(root, name.Value)));
        Assert.False(File.Exists(drawn));
    }

    [Fact]
    public async Task NothingBesideTheRecordingAskedForIsTouched()
    {
        RecordingId asked = RecordingId.New();
        RecordingId beside = RecordingId.New();
        RecordingFileName name = Holding(asked);
        RecordingFileName neighbour = Holding(beside);
        string neighbourDrawn = Drawn(beside);

        await Eraser().EraseAsync(asked, Primary, name, Cancel);

        Assert.True(File.Exists(Path.Combine(root, neighbour.Value)));
        Assert.True(File.Exists(neighbourDrawn));
    }

    [Fact]
    public async Task ARootNothingSaysWhereToFindIsRefusedRatherThanReadAsAlreadyGone()
    {
        RecordingId id = RecordingId.New();
        RecordingFileName name = Holding(id);

        RecordingErasure refused = await Eraser().EraseAsync(id, new OutputRoot("bulk"), name, Cancel);

        Assert.Equal(ErasureFault.RootOutOfReach, refused.Fault);
        Assert.Equal(0, refused.FilesRemoved);
        Assert.True(File.Exists(Path.Combine(root, name.Value)));
    }

    [Fact]
    public async Task ARootWithNoDirectoryOnItIsRefusedRatherThanReadAsAlreadyGone()
    {
        RecordingId id = RecordingId.New();
        RecordingFileName name = RecordingFileName.For(id, ".m2ts");
        string away = Path.Combine(Path.GetTempPath(), $"carina-erase-nothing-{Guid.NewGuid():N}");

        RecordingErasure refused = await EraserRootedAt(away).EraseAsync(id, Primary, name, Cancel);

        Assert.Equal(ErasureFault.RootOutOfReach, refused.Fault);
    }

    [Fact]
    public async Task ARootThatHoldsNothingAtAllIsRefusedBecauseThatIsWhatALostMountLooksLike()
    {
        RecordingId id = RecordingId.New();
        RecordingFileName name = RecordingFileName.For(id, ".m2ts");

        RecordingErasure refused = await Eraser().EraseAsync(id, Primary, name, Cancel);

        Assert.Equal(ErasureFault.RootOutOfReach, refused.Fault);
        Assert.Equal(0, refused.FilesRemoved);
    }

    [Fact]
    public async Task AFileAlreadyGoneFromARootThatStillHoldsRecordingsCountsAsRemoved()
    {
        RecordingId gone = RecordingId.New();
        Holding(RecordingId.New());

        RecordingErasure erased = await Eraser()
            .EraseAsync(gone, Primary, RecordingFileName.For(gone, ".m2ts"), Cancel);

        Assert.True(erased.EverythingIsGone);
        Assert.Equal(0, erased.FilesRemoved);
    }

    [Fact]
    public async Task ARecordingWithNoPictureDrawnYetIsStillThrownAway()
    {
        RecordingId id = RecordingId.New();
        RecordingFileName name = Holding(id);

        RecordingErasure erased = await Eraser().EraseAsync(id, Primary, name, Cancel);

        Assert.True(erased.EverythingIsGone);
        Assert.Equal(1, erased.FilesRemoved);
        Assert.False(File.Exists(Path.Combine(root, name.Value)));
    }

    [Fact]
    public async Task WhereNoDirectoryIsConfiguredForPicturesTheRecordingStillGoes()
    {
        RecordingId id = RecordingId.New();
        RecordingFileName name = Holding(id);
        string drawn = Drawn(id);

        RecordingErasure erased = await EraserWithNowhereForPictures().EraseAsync(id, Primary, name, Cancel);

        Assert.True(erased.EverythingIsGone);
        Assert.Equal(1, erased.FilesRemoved);
        Assert.False(File.Exists(Path.Combine(root, name.Value)));
        Assert.True(File.Exists(drawn));
    }

    [Fact]
    public async Task AFileThatWillNotComeOffTheDiskIsReportedAndLeftWhereItIs()
    {
        RecordingId id = RecordingId.New();
        RecordingFileName name = Holding(id);
        Holding(RecordingId.New());
        string held = Path.Combine(root, name.Value);
        File.Delete(held);
        Directory.CreateDirectory(held);

        RecordingErasure refused = await Eraser().EraseAsync(id, Primary, name, Cancel);

        Assert.Equal(ErasureFault.FileLeftBehind, refused.Fault);
        Assert.True(Directory.Exists(held));
    }

    [Fact]
    public async Task APictureThatWillNotComeOffTheDiskIsReportedEvenThoughTheRecordingWent()
    {
        RecordingId id = RecordingId.New();
        RecordingFileName name = Holding(id);
        Holding(RecordingId.New());
        Directory.CreateDirectory(Path.Combine(gallery, id.Wire + ThumbnailJob.Extension));

        RecordingErasure refused = await Eraser().EraseAsync(id, Primary, name, Cancel);

        Assert.Equal(ErasureFault.FileLeftBehind, refused.Fault);
        Assert.False(File.Exists(Path.Combine(root, name.Value)));
    }

    [Theory]
    [InlineData("a.m2ts", true)]
    [InlineData("down/a.m2ts", false)]
    [InlineData("../a.m2ts", false)]
    [InlineData("down/../../a.m2ts", false)]
    public void AFileIsOnlyReachedWhereItSitsDirectlyInTheRoom(string name, bool reached)
    {
        string room = Path.Combine(Path.GetTempPath(), "carina-room");

        Assert.Equal(reached, LocalRecordingFileEraser.LiesDirectlyUnder(room, Path.Combine(room, name)));
    }

    [Fact]
    public void TheRoomItselfIsNotAFileInIt()
    {
        string room = Path.Combine(Path.GetTempPath(), "carina-room");

        Assert.False(LocalRecordingFileEraser.LiesDirectlyUnder(room, room));
        Assert.False(LocalRecordingFileEraser.LiesDirectlyUnder(room, room + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void TheContainmentCheckIsATripWireAndNotTheGuarantee()
    {
        Assert.Throws<ArgumentException>(() => new RecordingFileName("down/a.m2ts"));
        Assert.Throws<ArgumentException>(() => new RecordingFileName("../a.m2ts"));
        Assert.Throws<ArgumentException>(() => new RecordingFileName("a/../../b.m2ts"));
    }

    private RecordingFileName Holding(RecordingId id)
    {
        RecordingFileName name = RecordingFileName.For(id, ".m2ts");
        File.WriteAllBytes(Path.Combine(root, name.Value), new byte[188]);

        return name;
    }

    private string Drawn(RecordingId id)
    {
        string path = Path.Combine(gallery, id.Wire + ThumbnailJob.Extension);
        File.WriteAllBytes(path, new byte[16]);

        return path;
    }

    private LocalRecordingFileEraser Eraser() => Built(root, gallery);

    private LocalRecordingFileEraser EraserWithNowhereForPictures() => Built(root, null);

    private LocalRecordingFileEraser EraserRootedAt(string room) => Built(room, gallery);

    private static LocalRecordingFileEraser Built(string room, string? pictures)
        => new(
            new IntegritySettings { OutputRoots = [new StorageRootPath(Primary, room)] },
            new ThumbnailSettings { WrittenTo = pictures },
            NullLogger<LocalRecordingFileEraser>.Instance);
}
