using System.Text;

using Carina.Domain.Captions;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Captions;
using Carina.Infrastructure.DataBroadcast;

namespace Carina.Infrastructure.Tests.DataBroadcast;

public sealed class DataBroadcastShelfTests : IDisposable
{
    private const int Entry = 0x40;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly string room = Directory.CreateTempSubdirectory("carina-data-broadcast-shelf-").FullName;

    private string Shelved => Path.Combine(room, "captions");

    public void Dispose() => Directory.Delete(room, recursive: true);

    [Fact(DisplayName = "BR-BD-005: a record is kept beside the captions under the recording's id and read back")]
    public async Task ARecordIsKeptBesideTheCaptionsAndReadBack()
    {
        DataBroadcastShelf shelf = Shelf();
        RecordingId id = RecordingId.New();

        await shelf.KeepAsync(id, Record("<bml>1</bml>"), Cancel);

        Assert.Equal(Path.Combine(Shelved, id.Wire + ".databroadcast"), shelf.PathOf(id));
        Assert.True(shelf.Holds(id));
        Assert.Equal("<bml>1</bml>", Body(await shelf.ReadAsync(id, Cancel)));
        Assert.Equal([id.Wire + ".databroadcast"], Directory.EnumerateFiles(Shelved).Select(Path.GetFileName));
    }

    [Fact(DisplayName = "BR-BD-005: a record kept again takes the place of the one before and leaves nothing unfinished")]
    public async Task ARecordKeptAgainTakesThePlaceOfTheOneBefore()
    {
        DataBroadcastShelf shelf = Shelf();
        RecordingId id = RecordingId.New();

        await shelf.KeepAsync(id, Record("<bml>1</bml>"), Cancel);
        await shelf.KeepAsync(id, Record("<bml>2</bml>"), Cancel);

        Assert.Equal("<bml>2</bml>", Body(await shelf.ReadAsync(id, Cancel)));
        Assert.Single(Directory.EnumerateFiles(Shelved));
    }

    [Fact(DisplayName = "BR-BD-005: a record that cannot be written leaves the one kept before and nothing unfinished")]
    public async Task ARecordThatCannotBeWrittenLeavesTheOneKeptBefore()
    {
        DataBroadcastShelf shelf = Shelf();
        RecordingId id = RecordingId.New();
        await shelf.KeepAsync(id, Record("<bml>1</bml>"), Cancel);

        await Assert.ThrowsAsync<ArgumentException>(() => shelf.KeepAsync(id, TooManyVersions(), Cancel));

        Assert.Equal("<bml>1</bml>", Body(await shelf.ReadAsync(id, Cancel)));
        Assert.Equal([id.Wire + ".databroadcast"], Directory.EnumerateFiles(Shelved).Select(Path.GetFileName));
    }

    [Fact(DisplayName = "BR-BD-005: a file on the shelf that is not a record reads as none, as does a record never kept")]
    public async Task AFileThatIsNotARecordReadsAsNone()
    {
        DataBroadcastShelf shelf = Shelf();
        RecordingId spoilt = RecordingId.New();
        Directory.CreateDirectory(Shelved);
        await File.WriteAllBytesAsync(shelf.PathOf(spoilt)!, Encoding.ASCII.GetBytes("CARINACC"), Cancel);

        Assert.Null(await shelf.ReadAsync(spoilt, Cancel));
        Assert.Null(await shelf.ReadAsync(RecordingId.New(), Cancel));
    }

    [Fact(DisplayName = "BR-BS-001: the shelf names the recordings it keeps a record for, and a record forgotten is gone")]
    public async Task TheShelfNamesTheRecordingsItKeepsARecordFor()
    {
        DataBroadcastShelf shelf = Shelf();
        RecordingId kept = RecordingId.New();
        RecordingId forgotten = RecordingId.New();
        await shelf.KeepAsync(kept, Record("<bml>1</bml>"), Cancel);
        await shelf.KeepAsync(forgotten, Record("<bml>1</bml>"), Cancel);
        await File.WriteAllBytesAsync(Path.Combine(Shelved, RecordingId.New().Wire + CaptionSettings.Extension), [0], Cancel);
        await File.WriteAllBytesAsync(Path.Combine(Shelved, RecordingId.New().Wire + DataBroadcastShelf.Extension + CaptionShelf.Unfinished), [0], Cancel);

        shelf.Forget(forgotten);
        shelf.Forget(RecordingId.New());

        Assert.Equal([kept.Wire], shelf.Shelved());
        Assert.False(shelf.Holds(forgotten));
    }

    [Fact]
    public void AShelfWithNowhereToKeepRecordsKeepsNothing()
    {
        DataBroadcastShelf shelf = new(new CaptionSettings());

        Assert.False(shelf.KeepsAnything);
        Assert.Null(shelf.PathOf(RecordingId.New()));
        Assert.Empty(shelf.Shelved());
        Assert.False(shelf.Holds(RecordingId.New()));
    }

    private DataBroadcastShelf Shelf() => new(new CaptionSettings { WrittenTo = Shelved });

    private static DataBroadcastRecord Record(string startup)
        => new(
            0,
            Entry,
            [new RecordedCarousel(Entry, 1, [new ModuleVersion(Entry, 0, 1, 0, 0, [new CarouselResource("startup.bml", "text/X-arib-bml", ResourceForm.Text, Encoding.UTF8.GetBytes(startup))])])],
            [],
            false);

    private static DataBroadcastRecord TooManyVersions()
        => new(
            0,
            Entry,
            [
                new RecordedCarousel(
                    Entry,
                    1,
                    [
                        .. Enumerable.Range(0, ushort.MaxValue + 1)
                            .Select(each => new ModuleVersion(Entry, each / 256, each % 256, 0, 0, [new CarouselResource("a", "image/jpeg", ResourceForm.Binary, new byte[] { 1 })])),
                    ]),
            ],
            [],
            false);

    private static string Body(DataBroadcastRecord? record)
        => Encoding.UTF8.GetString(record!.Carousels[0].Versions[0].Resources[0].Body.Span);
}
