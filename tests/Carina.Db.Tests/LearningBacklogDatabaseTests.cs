using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class LearningBacklogDatabaseTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Noon = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ExtractionVersion Newer = new(LearningData.ExtractionVersion + 1, ExtractionOrigin.RecordingFile);

    private static readonly ExtractionVersion Reduced = new(LearningData.ExtractionVersion, ExtractionOrigin.ReducedCopy);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private int nextEvent = 8_000;

    [Fact(DisplayName = "the recordings waiting to be read are the ended ones the record says wait or that have none, under the roots named, the most recently started first")]
    public async Task TheRecordingsWaitingAreThoseTheRecordSaysWait()
    {
        OutputRoot root = new($"backlog-{Guid.NewGuid():N}");
        List<(Recording Recording, LearningExtraction? Record)> kept = [];

        foreach ((LearningExtractionState State, ExtractionVersion Version, int Failures, TimeSpan SettledAfterTheStart) shape in Shapes())
        {
            Recording recording = await RecordedAsync(root, Noon.AddMinutes(-kept.Count * 7));
            LearningExtraction record = Record(recording, shape.State, shape.Version, shape.Failures, shape.SettledAfterTheStart);

            await AddAsync(record);
            kept.Add((recording, record));
        }

        Recording unread = await RecordedAsync(root, Noon.AddMinutes(5));
        Recording beingWritten = await RecordedAsync(root, Noon.AddMinutes(6), ended: false);
        Recording elsewhere = await RecordedAsync(new OutputRoot($"elsewhere-{Guid.NewGuid():N}"), Noon.AddMinutes(7));
        kept.Add((unread, null));

        IReadOnlyList<BackloggedRecording> awaiting = await AwaitingAsync(root, 100);

        RecordingId[] expected =
        [
            .. kept
                .Where(pair => pair.Record?.AwaitsReading(ExtractionVersion.Current, pair.Recording.StoppedAtActual) ?? true)
                .OrderByDescending(pair => pair.Recording.StartedAtActual)
                .Select(pair => pair.Recording.Id),
        ];
        Assert.Equal(7, expected.Length);
        Assert.Equal(expected, awaiting.Select(next => next.Recording.Id));
        Assert.Null(awaiting[0].Record);
        Assert.All(awaiting.Skip(1), next => Assert.Equal(next.Recording.Id, next.Record?.RecordingId));
        Assert.DoesNotContain(awaiting, next => next.Recording.Id.Equals(beingWritten.Id) || next.Recording.Id.Equals(elsewhere.Id));
        Assert.Equal(expected[..2], (await AwaitingAsync(root, 2)).Select(next => next.Recording.Id));
    }

    [Fact(DisplayName = "a record read or partway is given whether captions are shown once the captions are ready, and again once either changes")]
    public async Task ARecordIsGivenWhetherCaptionsAreShownOnceTheCaptionsAreReady()
    {
        OutputRoot root = new($"captions-{Guid.NewGuid():N}");
        DateTime captioned = Noon.AddHours(1);
        Recording ready = await CaptionedAsync(root, Noon, CaptionState.Ready, captioned);
        Recording partway = await CaptionedAsync(root, Noon.AddMinutes(-60), CaptionState.Ready, captioned);
        Recording given = await CaptionedAsync(root, Noon.AddMinutes(-120), CaptionState.Ready, captioned);
        Recording retaken = await CaptionedAsync(root, Noon.AddMinutes(-180), CaptionState.Ready, captioned.AddHours(2));
        Recording reread = await CaptionedAsync(root, Noon.AddMinutes(-240), CaptionState.Ready, captioned);
        Recording without = await CaptionedAsync(root, Noon.AddMinutes(-300), CaptionState.Absent, captioned);
        Recording pending = await CaptionedAsync(root, Noon.AddMinutes(-360), CaptionState.Pending, captioned);
        Recording nothingRead = await CaptionedAsync(root, Noon.AddMinutes(-420), CaptionState.Ready, captioned);
        Recording waiting = await CaptionedAsync(root, Noon.AddMinutes(-480), CaptionState.Ready, captioned);

        await AddAsync(Read(ready, LearningExtractionState.Done, TimeSpan.FromSeconds(1300), captioned.AddMinutes(-30)));
        await AddAsync(Read(partway, LearningExtractionState.Partial, TimeSpan.FromSeconds(300), captioned.AddMinutes(-30)));
        await AddAsync(Read(given, LearningExtractionState.Done, TimeSpan.FromSeconds(600), captioned.AddMinutes(-30)));
        await AddAsync(Read(retaken, LearningExtractionState.Done, TimeSpan.FromSeconds(600), captioned.AddMinutes(-30)));
        await AddAsync(Read(reread, LearningExtractionState.Done, TimeSpan.FromSeconds(600), captioned.AddHours(3)));
        await AddAsync(Read(without, LearningExtractionState.Done, TimeSpan.FromSeconds(600), captioned.AddMinutes(-30)));
        await AddAsync(Read(pending, LearningExtractionState.Done, TimeSpan.FromSeconds(600), captioned.AddMinutes(-30)));
        await AddAsync(Read(nothingRead, LearningExtractionState.Partial, TimeSpan.Zero, captioned.AddMinutes(-30)));
        await AddAsync(LearningExtraction.Waiting(waiting.Id, ProgrammeCopy.Of(waiting, null), Noon));

        foreach (Recording shown in new[] { given, retaken, reread })
        {
            await KeepAsync(LearningDataBlock.Of(shown.Id, LearningDataPart.OfCaptions(0, [0, 1, 1, 0]), ExtractionVersion.Current, captioned.AddHours(1)));
        }

        IReadOnlyList<LearningExtraction> uncaptioned = await UncaptionedAsync(100);

        Assert.Equal(
            [ready.Id, partway.Id, retaken.Id, reread.Id],
            uncaptioned.Select(record => record.RecordingId).Where(id => new[] { ready, partway, given, retaken, reread, without, pending, nothingRead, waiting }.Any(recording => recording.Id.Equals(id))));
        Assert.Single(await UncaptionedAsync(1));
    }

    [Fact(DisplayName = "whether captions are shown is kept and read back as a kind of learning data of its own")]
    public async Task WhetherCaptionsAreShownIsKept()
    {
        RecordingId recording = RecordingId.New();
        byte[] seconds = [.. Enumerable.Range(0, LearningData.ChunkSeconds).Select(second => second % 5 is 0 ? CaptionPresence.Shown : CaptionPresence.Hidden)];

        await KeepAsync(LearningDataBlock.Of(recording, LearningDataPart.OfCaptions(2, seconds), ExtractionVersion.Current, Noon));

        await using CarinaDbContext context = Context();
        LearningDataBlock? kept = await new LearningDataRepository(context).FindAsync(recording, LearningDataKind.CaptionPresence, 2, Cancel);

        Assert.NotNull(kept);
        Assert.True(kept.TryRead(out LearningDataPart? part));
        Assert.Equal(seconds, part.Captions.ToArray());
    }

    private static IEnumerable<(LearningExtractionState, ExtractionVersion, int, TimeSpan)> Shapes()
    {
        TimeSpan beforeTheEnd = TimeSpan.FromMinutes(10);
        TimeSpan afterTheEnd = TimeSpan.FromMinutes(45);

        yield return (LearningExtractionState.Waiting, ExtractionVersion.Current, 0, afterTheEnd);
        yield return (LearningExtractionState.Failed, ExtractionVersion.Current, LearningExtraction.MostRetries, afterTheEnd);
        yield return (LearningExtractionState.Failed, ExtractionVersion.Current, LearningExtraction.MostRetries + 1, afterTheEnd);
        yield return (LearningExtractionState.Done, ExtractionVersion.Current, 0, afterTheEnd);
        yield return (LearningExtractionState.Done, Reduced, 0, afterTheEnd);
        yield return (LearningExtractionState.Done, Newer, 0, afterTheEnd);
        yield return (LearningExtractionState.Partial, ExtractionVersion.Current, 0, beforeTheEnd);
        yield return (LearningExtractionState.Partial, ExtractionVersion.Current, 0, afterTheEnd);
        yield return (LearningExtractionState.Partial, Reduced, 0, afterTheEnd);
        yield return (LearningExtractionState.Following, ExtractionVersion.Current, 0, afterTheEnd);
    }

    private static LearningExtraction Record(
        Recording recording,
        LearningExtractionState state,
        ExtractionVersion version,
        int failures,
        TimeSpan settledAfterTheStart)
        => LearningExtraction.Rehydrate(
            recording.Id,
            state,
            version,
            TimeSpan.FromMinutes(5),
            [],
            null,
            failures > 0 ? new ExtractionFailureDetail(ExtractionFailure.Other, "it stopped") : null,
            failures,
            ProgrammeCopy.Of(recording, null),
            recording.StartedAtActual,
            recording.StartedAtActual + settledAfterTheStart);

    private static LearningExtraction Read(Recording recording, LearningExtractionState state, TimeSpan through, DateTime settled)
        => LearningExtraction.Rehydrate(
            recording.Id,
            state,
            ExtractionVersion.Current,
            through,
            [],
            null,
            null,
            0,
            ProgrammeCopy.Of(recording, null),
            recording.StartedAtActual,
            settled);

    private async Task<Recording> CaptionedAsync(OutputRoot root, DateTime startedAt, CaptionState state, DateTime captioned)
        => await RecordedAsync(root, startedAt, captions: (state, captioned));

    private async Task<Recording> RecordedAsync(
        OutputRoot root,
        DateTime startedAt,
        bool ended = true,
        (CaptionState State, DateTime At)? captions = null)
    {
        RecordingId id = RecordingId.New();
        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(40_003), new ServiceId(60_003), new EventId(++nextEvent), startedAt),
            root,
            RecordingFileName.For(id, ".m2ts"),
            startedAt,
            startedAt.AddMinutes(30),
            new ProgrammeSnapshot(
                "架空の番組",
                "架空のあらすじ",
                string.Empty,
                [new ProgrammeGenre(7, 0)],
                startedAt,
                AudioMode.Stereo,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            startedAt);

        if (ended)
        {
            recording.Wrote(TimeSpan.FromMinutes(30));
            recording.Abort(startedAt.AddMinutes(30));
            recording.Settle(RecordingOutcome.Complete, 3_400_000, startedAt.AddMinutes(30));
        }

        if (captions is { } taken)
        {
            recording.Caption(taken.State, taken.State is CaptionState.Ready ? 2 : null, taken.At);
        }

        await using CarinaDbContext context = Context();
        context.Add(recording);
        await context.SaveChangesAsync();

        return recording;
    }

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);

    private async Task AddAsync(LearningExtraction extraction)
    {
        await using CarinaDbContext context = Context();

        await new LearningExtractionRepository(context).AddAsync(extraction, Cancel);
    }

    private async Task KeepAsync(LearningDataBlock block)
    {
        await using CarinaDbContext context = Context();

        await new LearningDataRepository(context).KeepAsync(block, Cancel);
    }

    private async Task<IReadOnlyList<BackloggedRecording>> AwaitingAsync(OutputRoot root, int atMost)
    {
        await using CarinaDbContext context = Context();

        return await new LearningBacklogReader(context).AwaitingAsync([root], atMost, Cancel);
    }

    private async Task<IReadOnlyList<LearningExtraction>> UncaptionedAsync(int atMost)
    {
        await using CarinaDbContext context = Context();

        return await new LearningBacklogReader(context).UncaptionedAsync(atMost, Cancel);
    }
}
