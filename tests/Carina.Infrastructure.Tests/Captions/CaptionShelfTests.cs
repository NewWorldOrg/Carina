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
