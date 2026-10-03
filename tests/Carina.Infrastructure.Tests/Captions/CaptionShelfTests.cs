using Carina.Domain.Captions;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Captions;

namespace Carina.Infrastructure.Tests.Captions;

public sealed class CaptionShelfTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly string room = Directory.CreateTempSubdirectory("carina-shelf-").FullName;

    public void Dispose() => Directory.Delete(room, recursive: true);

    [Fact]
    public async Task BrPd016AKeptRecordIsOneFileNamedAfterTheRecordingAndNothingIsLeftBesideIt()
    {
        RecordingId id = RecordingId.New();
        CaptionShelf shelf = Shelf();

        await shelf.KeepAsync(id, Record(3), Cancel);

        Assert.Equal([id.Wire + ".captions"], Directory.GetFiles(Shelved()).Select(Path.GetFileName));
        Assert.True(shelf.Holds(id));
        Assert.Equal(3, (await shelf.ReadAsync(id, Cancel))!.Cues.Count);
    }

    [Fact]
    public async Task KeepingAgainReplacesWhatWasKept()
    {
        RecordingId id = RecordingId.New();
        CaptionShelf shelf = Shelf();
        await shelf.KeepAsync(id, Record(3), Cancel);

        await shelf.KeepAsync(id, Record(1), Cancel);

        Assert.Single((await shelf.ReadAsync(id, Cancel))!.Cues);
    }

    [Fact]
    public async Task BrPd017WhereTheFilesClockBeganIsReadWithoutReadingTheWholeRecord()
    {
        RecordingId id = RecordingId.New();
        CaptionShelf shelf = Shelf();
        await shelf.KeepAsync(id, Record(3), Cancel);

        Assert.Equal(TimeSpan.FromSeconds(1), await shelf.StartsAtAsync(id, Cancel));
        Assert.Null(await shelf.StartsAtAsync(RecordingId.New(), Cancel));
    }

    [Fact]
    public async Task AShelfWithNoDirectoryReadsAsHoldingNothing()
    {
        CaptionShelf shelf = new(new CaptionSettings());

        Assert.Null(await shelf.ReadAsync(RecordingId.New(), Cancel));
        Assert.Null(await shelf.StartsAtAsync(RecordingId.New(), Cancel));
    }

    [Fact]
    public async Task ARecordThatIsNotThereReadsAsNothing()
        => Assert.Null(await Shelf().ReadAsync(RecordingId.New(), Cancel));

    [Fact]
    public async Task AFileThatIsNotARecordReadsAsNothing()
    {
        RecordingId id = RecordingId.New();
        Directory.CreateDirectory(Shelved());
        await File.WriteAllTextAsync(Path.Combine(Shelved(), id.Wire + ".captions"), "garbage", Cancel);

        Assert.Null(await Shelf().ReadAsync(id, Cancel));
    }

    [Fact]
    public async Task ForgettingTakesTheRecordOffTheShelfAndForgettingNothingIsNoFault()
    {
        RecordingId id = RecordingId.New();
        CaptionShelf shelf = Shelf();
        await shelf.KeepAsync(id, Record(1), Cancel);

        shelf.Forget(id);
        shelf.Forget(id);

        Assert.False(shelf.Holds(id));
    }

    [Fact]
    public async Task TheShelfNamesTheRecordingsItKeepsARecordForAndNothingElse()
    {
        RecordingId first = RecordingId.New();
        RecordingId second = RecordingId.New();
        CaptionShelf shelf = Shelf();
        await shelf.KeepAsync(first, Record(1), Cancel);
        await shelf.KeepAsync(second, Record(1), Cancel);
        await File.WriteAllTextAsync(Path.Combine(Shelved(), "beside.jpg"), "x", Cancel);
        await File.WriteAllTextAsync(Path.Combine(Shelved(), first.Wire + ".captions.part"), "x", Cancel);

        Assert.Equal(new[] { first.Wire, second.Wire }.Order(StringComparer.Ordinal), shelf.Shelved().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task BrPd016TheShelfNamesTheRecordsKeptBeforeTheTextWasTakenAndNoOthers()
    {
        RecordingId textless = RecordingId.New();
        RecordingId withText = RecordingId.New();
        RecordingId notARecord = RecordingId.New();
        CaptionShelf shelf = Shelf();
        await shelf.KeepAsync(textless, Record(1), Cancel);
        await shelf.KeepAsync(withText, new CaptionRecord(1440, 1080, TimeSpan.Zero, [], [new CaptionLine(1, "字")]), Cancel);
        await File.WriteAllTextAsync(Path.Combine(Shelved(), notARecord.Wire + ".captions"), "not a record", Cancel);

        Assert.Equal([textless.Wire], shelf.Textless());
    }

    [Fact]
    public void AShelfThatWasNeverWrittenToNamesNothingToTakeAgain()
        => Assert.Empty(Shelf().Textless());

    [Fact]
    public void AShelfThatWasNeverWrittenToNamesNothing()
        => Assert.Empty(Shelf().Shelved());

    [Fact]
    public async Task ARecordThatCannotBeMovedIntoPlaceLeavesNothingHalfWrittenBehind()
    {
        RecordingId id = RecordingId.New();
        Directory.CreateDirectory(Path.Combine(Shelved(), id.Wire + ".captions", "in-the-way"));

        await Assert.ThrowsAnyAsync<IOException>(() => Shelf().KeepAsync(id, Record(1), Cancel));

        Assert.False(File.Exists(Path.Combine(Shelved(), id.Wire + ".captions.part")));
    }

    [Fact]
    public void AShelfWithNoDirectoryRefusesToSayWhereARecordWouldBe()
        => Assert.Throws<InvalidOperationException>(() => new CaptionShelf(new CaptionSettings()).Holds(RecordingId.New()));

    private string Shelved() => Path.Combine(room, "captions");

    private CaptionShelf Shelf() => new(new CaptionSettings { WrittenTo = Shelved() });

    private static CaptionRecord Record(int changes)
        => new(
            1440,
            1080,
            TimeSpan.FromSeconds(1),
            [.. Enumerable.Range(0, changes).Select(at => new CaptionCue(90_000L * at, new CaptionPlacement(0, 0, 1, 1, new byte[] { 1 })))]);
}
