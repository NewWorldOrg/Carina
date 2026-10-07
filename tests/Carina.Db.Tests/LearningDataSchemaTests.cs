using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class LearningDataSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const int Network = 40_001;

    private static readonly DateTime Noon = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string New = char.ConvertFromUtf32(0x1F21F);

    private static readonly ExtractionVersion Newer = new(LearningData.ExtractionVersion + 1, ExtractionOrigin.RecordingFile);

    private static readonly ExtractionVersion Reduced = new(LearningData.ExtractionVersion, ExtractionOrigin.ReducedCopy);

    [Fact(DisplayName = "the learning data and its record outlive the recording they were taken from")]
    public async Task TheLearningDataOutlivesTheRecording()
    {
        Recording recording = await RecordedAsync(7_001);
        ProgrammeCopy copy = ProgrammeCopy.Of(recording, Noon.AddMinutes(30));
        LearningExtraction extraction = LearningExtraction.Following(recording.Id, copy, ExtractionVersion.Current, Noon);
        LearningDataBlock block = Block(recording.Id, LearningDataKind.SoundFingerprints, 0, ExtractionVersion.Current, 3);

        await AddAsync(extraction);
        await KeepAsync(block);

        await using (NpgsqlConnection connection = await database.OpenAsync())
        {
            await using var erasing = new NpgsqlCommand("DELETE FROM recording WHERE id = @recording", connection);
            erasing.Parameters.AddWithValue("recording", recording.Id.Value);
            Assert.Equal(1, await erasing.ExecuteNonQueryAsync());
        }

        LearningExtraction? kept = await FoundAsync(recording.Id);
        LearningDataBlock? keptBlock = await FoundBlockAsync(recording.Id, LearningDataKind.SoundFingerprints, 0);

        Assert.NotNull(kept);
        Assert.Equal(copy, kept.Programme);
        Assert.Equal([ProgrammeMark.New], kept.Programme.Marks);
        Assert.NotNull(keptBlock);
        Assert.Equal(block.Bytes, keptBlock.Bytes);
        Assert.Equal(1, await CountAsync(recording.Id));
    }

    [Fact(DisplayName = "writing the same chunk again replaces it, and adds no row")]
    public async Task WritingTheSameChunkAgainReplacesIt()
    {
        var recording = new RecordingId(Guid.NewGuid());
        LearningDataBlock first = Block(recording, LearningDataKind.Loudness, 4, ExtractionVersion.Current, 3);
        LearningDataBlock again = Block(recording, LearningDataKind.Loudness, 4, Newer, 40, Noon.AddHours(1));

        await KeepAsync(first);
        await KeepAsync(again);

        LearningDataBlock? kept = await FoundBlockAsync(recording, LearningDataKind.Loudness, 4);

        Assert.Equal(1, await CountAsync(recording));
        Assert.NotNull(kept);
        Assert.Equal(again.Bytes, kept.Bytes);
        Assert.Equal(Newer, kept.Version);
        Assert.Equal(Noon.AddHours(1), kept.WrittenAt);
        Assert.True(kept.TryRead(out LearningDataPart? part));
        Assert.Equal(40, part.Count);
    }

    [Fact(DisplayName = "each kind and chunk of a recording is a row of its own, counted per recording")]
    public async Task EachKindAndChunkIsARowOfItsOwn()
    {
        var recording = new RecordingId(Guid.NewGuid());
        var other = new RecordingId(Guid.NewGuid());

        foreach (LearningDataKind kind in new[] { LearningDataKind.Loudness, LearningDataKind.SoundFingerprints })
        {
            await KeepAsync(Block(recording, kind, 0, ExtractionVersion.Current, 2));
            await KeepAsync(Block(recording, kind, 1, ExtractionVersion.Current, 2));
        }

        await KeepAsync(Block(other, LearningDataKind.Loudness, 0, ExtractionVersion.Current, 2));

        Assert.Equal(4, await CountAsync(recording));
        Assert.Equal(1, await CountAsync(other));
        Assert.Equal(0, await CountAsync(new RecordingId(Guid.NewGuid())));
    }

    [Fact(DisplayName = "the size of the learning data is the bytes kept, and a chunk written again counts once")]
    public async Task TheSizeIsTheBytesKept()
    {
        var recording = new RecordingId(Guid.NewGuid());
        LearningDataBlock first = Block(recording, LearningDataKind.Loudness, 0, ExtractionVersion.Current, 3);
        LearningDataBlock again = Block(recording, LearningDataKind.Loudness, 0, ExtractionVersion.Current, 300);
        long before = await SizeAsync();

        await KeepAsync(first);
        Assert.Equal(before + first.Bytes.Length, await SizeAsync());

        await KeepAsync(again);
        Assert.Equal(before + again.Bytes.Length, await SizeAsync());
    }

    [Fact(DisplayName = "an extraction reads back as it was written")]
    public async Task AnExtractionReadsBackAsItWasWritten()
    {
        RecordingId recording = RecordingId.New();
        var gap = new LearningDataGap(TimeSpan.FromSeconds(12.5), TimeSpan.FromSeconds(30));
        await AddAsync(LearningExtraction.Following(recording, Copy(Noon, "架空の番組"), Reduced, Noon));

        LearningExtractionWrite written = await ChangeAsync(recording, extraction =>
        {
            extraction.Opened(new ExtractionSound(1, TimeSpan.FromMilliseconds(-120)), Noon.AddMinutes(1));
            extraction.Missed(gap, Noon.AddMinutes(2));
            extraction.Reached(TimeSpan.FromMinutes(25), Noon.AddMinutes(3));
            extraction.Fail(ExtractionFailure.TimingMismatch, "the times do not line up", Noon.AddMinutes(4));
            extraction.Retry(Noon.AddMinutes(5));
        });

        Assert.Equal(LearningExtractionWrite.Written, written);

        LearningExtraction? kept = await FoundAsync(recording);

        Assert.NotNull(kept);
        Assert.Equal(LearningExtractionState.Waiting, kept.State);
        Assert.Equal(Reduced, kept.Version);
        Assert.Equal(TimeSpan.FromMinutes(25), kept.ReadThrough);
        Assert.Equal([gap], kept.Gaps);
        Assert.Equal(new ExtractionSound(1, TimeSpan.FromMilliseconds(-120)), kept.Sound);
        Assert.Equal(new ExtractionFailureDetail(ExtractionFailure.TimingMismatch, "the times do not line up"), kept.Failure);
        Assert.Equal(1, kept.Failures);
        Assert.Equal(Copy(Noon, "架空の番組"), kept.Programme);
        Assert.Equal(Noon, kept.CreatedAt);
        Assert.Equal(Noon.AddMinutes(5), kept.UpdatedAt);
    }

    [Fact(DisplayName = "a recording not yet read is kept with no version")]
    public async Task ARecordingNotYetReadIsKeptWithNoVersion()
    {
        RecordingId recording = RecordingId.New();

        await AddAsync(LearningExtraction.Waiting(recording, Copy(Noon, "架空の番組"), Noon));

        LearningExtraction? kept = await FoundAsync(recording);

        Assert.NotNull(kept);
        Assert.Null(kept.Version);
        Assert.Null(kept.Sound);
        Assert.Null(kept.Failure);
        Assert.Null(kept.Programme.Episode);
        Assert.Null(kept.Programme.SeriesName);
    }

    [Fact(DisplayName = "the table refuses a state it does not know")]
    public async Task TheTableRefusesAStateItDoesNotKnow()
    {
        RecordingId recording = RecordingId.New();
        await AddAsync(LearningExtraction.Waiting(recording, Copy(Noon, "架空の番組"), Noon));

        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var moving = new NpgsqlCommand(
            "UPDATE segment_extraction SET state = 'Sleeping' WHERE recording_id = @recording",
            connection);
        moving.Parameters.AddWithValue("recording", recording.Value);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => moving.ExecuteNonQueryAsync());

        Assert.Equal("ck_segment_extraction_state", refusal.ConstraintName);
    }

    [Fact(DisplayName = "the table refuses a failure that is not counted, and a read through that is counted")]
    public async Task TheTableRefusesFailuresThatDoNotAddUp()
    {
        RecordingId recording = RecordingId.New();
        await AddAsync(LearningExtraction.Waiting(recording, Copy(Noon, "架空の番組"), Noon));

        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException uncounted = await Assert.ThrowsAsync<PostgresException>(
            () => UpdateAsync(connection, recording, "failure = 'Other', failure_reason = 'x', failures = 0"));
        PostgresException counted = await Assert.ThrowsAsync<PostgresException>(
            () => UpdateAsync(connection, recording, "state = 'Done', version_number = 1, version_origin = 'RecordingFile', failure = 'Other', failure_reason = 'x', failures = 1"));

        Assert.Equal("ck_segment_extraction_failure", uncounted.ConstraintName);
        Assert.Equal("ck_segment_extraction_failure", counted.ConstraintName);
    }

    [Fact(DisplayName = "the table refuses a kind of learning data it does not keep")]
    public async Task TheTableRefusesAKindItDoesNotKeep()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var writing = new NpgsqlCommand(
            """
            INSERT INTO segment_learning_data (recording_id, kind, chunk, bytes, version_number, version_origin, written_at)
            VALUES (@recording, 9, 0, '\x01'::bytea, 1, 'RecordingFile', timestamptz '2026-10-01 12:00:00+00')
            """,
            connection);
        writing.Parameters.AddWithValue("recording", Guid.NewGuid());

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => writing.ExecuteNonQueryAsync());

        Assert.Equal("ck_segment_learning_data_kind", refusal.ConstraintName);
    }

    [Fact(DisplayName = "only one recording is read at a time")]
    public async Task OnlyOneRecordingIsReadAtATime()
    {
        RecordingId first = RecordingId.New();
        RecordingId second = RecordingId.New();
        await AddAsync(LearningExtraction.Waiting(first, Copy(Noon, "架空の番組"), Noon));
        await AddAsync(LearningExtraction.Waiting(second, Copy(Noon, "架空の番組"), Noon));

        Assert.Equal(
            LearningExtractionWrite.Written,
            await ChangeAsync(first, extraction => extraction.Read(ExtractionVersion.Current, Noon.AddMinutes(1))));
        Assert.Equal(
            LearningExtractionWrite.AnotherIsReading,
            await ChangeAsync(second, extraction => extraction.Read(ExtractionVersion.Current, Noon.AddMinutes(1))));
        Assert.Equal(LearningExtractionState.Waiting, (await FoundAsync(second))!.State);

        await using (NpgsqlConnection connection = await database.OpenAsync())
        {
            PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
                () => UpdateAsync(connection, second, "state = 'Reading', version_number = 1, version_origin = 'RecordingFile'"));

            Assert.Equal("ux_segment_extraction_reading", refusal.ConstraintName);
        }

        Assert.Equal(
            LearningExtractionWrite.Written,
            await ChangeAsync(first, extraction => extraction.Finish(Noon.AddMinutes(2))));
        Assert.Equal(
            LearningExtractionWrite.Written,
            await ChangeAsync(second, extraction => extraction.Read(ExtractionVersion.Current, Noon.AddMinutes(3))));
        Assert.Equal(
            LearningExtractionWrite.Written,
            await ChangeAsync(second, extraction => extraction.Finish(Noon.AddMinutes(4))));
    }

    [Fact(DisplayName = "of two writers who read the same extraction, the one who saves second is refused")]
    public async Task OfTwoWritersTheOneWhoSavesSecondIsRefused()
    {
        RecordingId recording = RecordingId.New();
        await AddAsync(LearningExtraction.Following(recording, Copy(Noon, "架空の番組"), ExtractionVersion.Current, Noon));

        await using CarinaDbContext followingContext = Context();
        await using CarinaDbContext switchingContext = Context();
        var following = new LearningExtractionRepository(followingContext);
        var switching = new LearningExtractionRepository(switchingContext);
        LearningExtraction followed = (await following.FindAsync(recording, CancellationToken.None))!;
        LearningExtraction switched = (await switching.FindAsync(recording, CancellationToken.None))!;

        switched.Pause(Noon.AddMinutes(2));
        Assert.Equal(LearningExtractionWrite.Written, await switching.SaveAsync(switched, CancellationToken.None));

        followed.Reached(TimeSpan.FromMinutes(5), Noon.AddMinutes(3));
        LearningExtractionMovedMeanwhileException refusal = await Assert.ThrowsAsync<LearningExtractionMovedMeanwhileException>(
            () => following.SaveAsync(followed, CancellationToken.None));

        LearningExtraction? kept = await FoundAsync(recording);

        Assert.Equal(recording, refusal.RecordingId);
        Assert.NotNull(kept);
        Assert.Equal(LearningExtractionState.Waiting, kept.State);
        Assert.Equal(TimeSpan.Zero, kept.ReadThrough);
    }

    [Fact(DisplayName = "extractions are listed by state, the most recently recorded first")]
    public async Task ExtractionsAreListedByStateTheMostRecentlyRecordedFirst()
    {
        DateTime later = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        RecordingId oldest = RecordingId.New();
        RecordingId newest = RecordingId.New();
        RecordingId between = RecordingId.New();

        await AddAsync(LearningExtraction.Waiting(oldest, Copy(later, "古い回"), Noon));
        await AddAsync(LearningExtraction.Waiting(newest, Copy(later.AddDays(2), "新しい回"), Noon));
        await AddAsync(LearningExtraction.Waiting(between, Copy(later.AddDays(1), "間の回"), Noon));
        await AddAsync(LearningExtraction.Following(RecordingId.New(), Copy(later.AddDays(3), "録画中の回"), ExtractionVersion.Current, Noon));

        await using CarinaDbContext context = Context();
        IReadOnlyList<LearningExtraction> waiting = await new LearningExtractionRepository(context)
            .ListAsync(LearningExtractionState.Waiting, 3, CancellationToken.None);

        Assert.Equal([newest, between, oldest], waiting.Select(extraction => extraction.RecordingId));
    }

    private static ProgrammeCopy Copy(DateTime recordedAt, string name)
        => new(
            new NetworkId(Network),
            new ServiceId(60_001),
            recordedAt,
            recordedAt.AddMinutes(30),
            recordedAt,
            name,
            [new ProgrammeGenre(7, 0)],
            [ProgrammeMark.Captioned, ProgrammeMark.New],
            null,
            AudioMode.Stereo,
            null);

    private static LearningDataBlock Block(
        RecordingId recording,
        LearningDataKind kind,
        int chunk,
        ExtractionVersion version,
        int readings,
        DateTime? at = null)
    {
        LearningDataChunk sample = LearningDataChunk.Of(
            chunk,
            FrameClock.Of(30000, 1001, LearningData.ChunkStarts(chunk)),
            [.. Enumerable.Range(0, readings).Select(reading => (uint)reading * 2_654_435_761u)],
            [.. Enumerable.Range(0, readings).Select(reading => (byte)(reading % 200))],
            [],
            [],
            []);

        return LearningDataBlock.Of(recording, LearningDataPart.Of(sample, kind), version, at ?? Noon);
    }

    private static async Task UpdateAsync(NpgsqlConnection connection, RecordingId recording, string assignments)
    {
        await using var moving = new NpgsqlCommand(
            $"UPDATE segment_extraction SET {assignments} WHERE recording_id = @recording",
            connection);
        moving.Parameters.AddWithValue("recording", recording.Value);

        await moving.ExecuteNonQueryAsync();
    }

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);

    private async Task AddAsync(LearningExtraction extraction)
    {
        await using CarinaDbContext context = Context();

        await new LearningExtractionRepository(context).AddAsync(extraction, CancellationToken.None);
    }

    private async Task<LearningExtractionWrite> ChangeAsync(RecordingId recording, Action<LearningExtraction> change)
    {
        await using CarinaDbContext context = Context();
        var repository = new LearningExtractionRepository(context);
        LearningExtraction extraction = (await repository.FindAsync(recording, CancellationToken.None))!;

        change(extraction);

        return await repository.SaveAsync(extraction, CancellationToken.None);
    }

    private async Task<LearningExtraction?> FoundAsync(RecordingId recording)
    {
        await using CarinaDbContext context = Context();

        return await new LearningExtractionRepository(context).FindAsync(recording, CancellationToken.None);
    }

    private async Task KeepAsync(LearningDataBlock block)
    {
        await using CarinaDbContext context = Context();

        await new LearningDataRepository(context).KeepAsync(block, CancellationToken.None);
    }

    private async Task<LearningDataBlock?> FoundBlockAsync(RecordingId recording, LearningDataKind kind, int chunk)
    {
        await using CarinaDbContext context = Context();

        return await new LearningDataRepository(context).FindAsync(recording, kind, chunk, CancellationToken.None);
    }

    private async Task<int> CountAsync(RecordingId recording)
    {
        await using CarinaDbContext context = Context();

        return await new LearningDataRepository(context).CountAsync(recording, CancellationToken.None);
    }

    private async Task<long> SizeAsync()
    {
        await using CarinaDbContext context = Context();

        return await new LearningDataRepository(context).SizeAsync(CancellationToken.None);
    }

    private async Task<Recording> RecordedAsync(int eventId)
    {
        RecordingId id = RecordingId.New();
        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(Network), new ServiceId(60_001), new EventId(eventId), Noon),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            Noon,
            Noon.AddHours(1),
            new ProgrammeSnapshot(
                $"架空の番組{New}",
                "架空のあらすじ",
                string.Empty,
                [new ProgrammeGenre(7, 0)],
                Noon,
                AudioMode.Stereo,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            Noon);

        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, 3_400_000, Noon.AddMinutes(30));

        await using CarinaDbContext context = Context();
        context.Add(recording);
        await context.SaveChangesAsync();

        return recording;
    }
}
