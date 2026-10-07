using System.Security.Cryptography;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Segments;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class LearningDataAmountReaderTests(RepositoryDatabase database)
{
    private static readonly DateTime Noon = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly ExtractionFailureDetail Unreadable = new(ExtractionFailure.Other, "unreadable");

    private int nextEvent = 9_000;

    [Fact(DisplayName = "only recordings read to the end or partway hold data, counted with how long of them was read")]
    public async Task OnlyRecordingsReadToTheEndOrPartwayHoldData()
    {
        LearningDataAmount before = await ReadAsync();

        await AddAsync(RecordingId.New(), LearningExtractionState.Done, TimeSpan.FromMinutes(30));
        await AddAsync(RecordingId.New(), LearningExtractionState.Partial, TimeSpan.FromSeconds(630));
        await AddAsync(RecordingId.New(), LearningExtractionState.Following, TimeSpan.FromMinutes(5));
        await AddAsync(RecordingId.New(), LearningExtractionState.Waiting, TimeSpan.Zero);
        await AddAsync(RecordingId.New(), LearningExtractionState.Failed, TimeSpan.Zero);

        LearningDataAmount after = await ReadAsync();

        Assert.Equal(before.Recordings + 2, after.Recordings);
        Assert.Equal(before.Duration + TimeSpan.FromSeconds(2_430), after.Duration);
    }

    [Fact(DisplayName = "throwing the recordings away changes none of the figures of the data taken from them")]
    public async Task ThrowingTheRecordingsAwayChangesNoneOfTheFigures()
    {
        Recording read = await EndedAsync();
        Recording partway = await EndedAsync();
        await AddAsync(read.Id, LearningExtractionState.Done, TimeSpan.FromMinutes(20));
        await AddAsync(partway.Id, LearningExtractionState.Partial, TimeSpan.FromMinutes(5));
        LearningDataAmount kept = await ReadAsync();

        await ThrowAwayAsync(read.Id);
        await ThrowAwayAsync(partway.Id);
        LearningDataAmount after = await ReadAsync();

        Assert.Equal((kept.Recordings, kept.Duration, kept.Bytes), (after.Recordings, after.Duration, after.Bytes));
    }

    [Fact(DisplayName = "an ended recording with no record, or with one that waits to be read from the file, is waiting to be read")]
    public async Task AnEndedRecordingWithNoRecordOrAWaitingOneIsWaiting()
    {
        LearningDataAmount before = await ReadAsync();

        await EndedAsync();
        Recording waiting = await EndedAsync();
        Recording failed = await EndedAsync();
        Recording spent = await EndedAsync();
        Recording done = await EndedAsync();
        await AddAsync(waiting.Id, LearningExtractionState.Waiting, TimeSpan.Zero);
        await AddAsync(failed.Id, LearningExtractionState.Failed, TimeSpan.Zero);
        await AddAsync(spent.Id, LearningExtractionState.Failed, TimeSpan.Zero, LearningExtraction.MostRetries + 1);
        await AddAsync(done.Id, LearningExtractionState.Done, TimeSpan.FromMinutes(30));

        LearningDataAmount after = await ReadAsync();

        Assert.Equal(before.Waiting + 3, after.Waiting);
        Assert.Equal(before.Recordings + 1, after.Recordings);
    }

    [Fact(DisplayName = "a recording still being recorded is not waiting, with or without a record")]
    public async Task ARecordingStillBeingRecordedIsNotWaiting()
    {
        LearningDataAmount before = await ReadAsync();

        await InFlightAsync();
        Recording followed = await InFlightAsync();
        await AddAsync(followed.Id, LearningExtractionState.Waiting, TimeSpan.Zero);

        Assert.Equal(before.Waiting, (await ReadAsync()).Waiting);
    }

    [Fact(DisplayName = "a waiting record whose recording was thrown away is no longer waiting, since nothing is left to read")]
    public async Task AWaitingRecordWhoseRecordingWasThrownAwayIsNoLongerWaiting()
    {
        LearningDataAmount before = await ReadAsync();
        Recording waiting = await EndedAsync();
        await AddAsync(waiting.Id, LearningExtractionState.Waiting, TimeSpan.Zero);

        Assert.Equal(before.Waiting + 1, (await ReadAsync()).Waiting);

        await ThrowAwayAsync(waiting.Id);

        Assert.Equal(before.Waiting, (await ReadAsync()).Waiting);
    }

    [Fact(DisplayName = "the room the learning data takes is what the segment tables take on disk, indexes and all")]
    public async Task TheRoomTakenIsWhatTheSegmentTablesTakeOnDisk()
    {
        LearningDataAmount before = await ReadAsync();

        Assert.Equal(await OnDiskAsync(), before.Bytes);

        const int Megabyte = 1 << 20;
        await WriteNoiseAsync(RandomNumberGenerator.GetBytes(Megabyte));
        LearningDataAmount after = await ReadAsync();

        Assert.Equal(await OnDiskAsync(), after.Bytes);
        Assert.True(after.Bytes >= before.Bytes + Megabyte, $"{after.Bytes} is not a megabyte more than {before.Bytes}");
    }

    private static ProgrammeCopy Copy(DateTime recordedAt)
        => new(
            new NetworkId(40_001),
            new ServiceId(60_001),
            recordedAt,
            recordedAt.AddMinutes(30),
            recordedAt,
            "架空の番組",
            [new ProgrammeGenre(7, 0)],
            [],
            null,
            AudioMode.Stereo,
            null);

    private static LearningExtraction Extraction(RecordingId recording, LearningExtractionState state, TimeSpan readThrough, int failures)
    {
        bool failed = state is LearningExtractionState.Failed;
        ExtractionVersion? version = state is LearningExtractionState.Waiting ? null : ExtractionVersion.Current;

        return LearningExtraction.Rehydrate(
            recording,
            state,
            version,
            readThrough,
            [],
            null,
            failed ? Unreadable : null,
            failed ? failures : 0,
            Copy(Noon),
            Noon,
            Noon);
    }

    private Recording Begun()
    {
        RecordingId id = RecordingId.New();

        return Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(40_001), new ServiceId(60_001), new EventId(nextEvent++), Noon),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".ts"),
            Noon,
            Noon.AddMinutes(30),
            new ProgrammeSnapshot(
                "架空の番組",
                string.Empty,
                string.Empty,
                [],
                Noon.AddHours(-6),
                AudioMode.Stereo,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            Noon);
    }

    private async Task<Recording> InFlightAsync()
    {
        Recording begun = Begun();

        await using CarinaDbContext writing = database.Open();
        await new RecordingRepository(writing).AddAsync(begun, Cancel);

        return begun;
    }

    private async Task<Recording> EndedAsync()
    {
        Recording begun = Begun();

        await using CarinaDbContext writing = database.Open();
        var repository = new RecordingRepository(writing);
        await repository.AddAsync(begun, Cancel);

        begun.Abort(Noon.AddMinutes(30));
        begun.Settle(RecordingOutcome.Complete, 1_000_000, Noon.AddMinutes(30));
        await repository.SaveAsync(begun, Cancel);

        return begun;
    }

    private async Task AddAsync(RecordingId recording, LearningExtractionState state, TimeSpan readThrough, int failures = 1)
    {
        await using CarinaDbContext writing = database.Open();
        await new LearningExtractionRepository(writing).AddAsync(Extraction(recording, state, readThrough, failures), Cancel);
    }

    private async Task ThrowAwayAsync(RecordingId recording)
    {
        await using CarinaDbContext writing = database.Open();

        Assert.Equal(1, await writing.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM recording WHERE id = {recording.Value}", Cancel));
    }

    private async Task WriteNoiseAsync(byte[] noise)
    {
        Guid recording = Guid.NewGuid();
        short kind = (short)LearningDataKind.SoundFingerprints;
        int number = ExtractionVersion.Current.Number;
        string origin = ExtractionVersion.Current.Origin.ToString();

        await using CarinaDbContext writing = database.Open();
        await writing.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO segment_learning_data (recording_id, kind, chunk, bytes, version_number, version_origin, written_at)
            VALUES ({recording}, {kind}, {0}, {noise}, {number}, {origin}, {Noon})
            """,
            Cancel);
    }

    private async Task<long> OnDiskAsync()
    {
        await using CarinaDbContext reading = database.Open();

        return await reading.Database
            .SqlQueryRaw<long>(
                """
                SELECT sum(pg_total_relation_size(name::regclass))::bigint AS "Value"
                FROM unnest(ARRAY['segment_extraction', 'segment_learning_data', 'segment_settings']) AS name
                """)
            .SingleAsync(Cancel);
    }

    private async Task<LearningDataAmount> ReadAsync()
    {
        await using CarinaDbContext reading = database.Open();

        return await new LearningDataAmountReader(reading).ReadAsync(Cancel);
    }
}
