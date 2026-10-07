using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Configurations;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Tests.Segments;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SegmentSettingsRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime Noon = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "a database nobody has switched learning on holds no row at all")]
    public async Task ADatabaseNobodyHasSwitchedHoldsNoRow()
    {
        await ClearAsync();

        await using CarinaDbContext reading = database.Open();

        Assert.Null(await new SegmentSettingsRepository(reading).ReadAsync(Cancel));
    }

    [Fact(DisplayName = "what was switched is read back as it was written, and switching again keeps one row")]
    public async Task WhatWasSwitchedIsReadBackAndSwitchingAgainKeepsOneRow()
    {
        await ClearAsync();

        await SaveAsync(SegmentSettings.LearningSwitched(true, Noon));
        await SaveAsync(SegmentSettings.LearningSwitched(false, Noon.AddHours(3)));

        await using CarinaDbContext reading = database.Open();
        SegmentSettings? held = await new SegmentSettingsRepository(reading).ReadAsync(Cancel);

        Assert.NotNull(held);
        Assert.Equal(SegmentSettings.TheOnlyRow, held.Id);
        Assert.False(held.Learning);
        Assert.Equal(Noon.AddHours(3), held.LearningChangedAt);
        Assert.Equal(1, await reading.Set<SegmentSettings>().CountAsync(Cancel));
    }

    [Fact(DisplayName = "the table itself refuses a second row")]
    public async Task TheTableItselfRefusesASecondRow()
    {
        await ClearAsync();
        await SaveAsync(SegmentSettings.LearningSwitched(true, Noon));

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => RunAsync($"INSERT INTO {SegmentSettingsConfiguration.TableName} "
                + "(id, learning, learning_changed_at) VALUES (2, true, now())"));

        Assert.Equal(SegmentSettingsConfiguration.SingleRowCheck, refusal.ConstraintName);
    }

    [Fact(DisplayName = "switching learning off and on again leaves every row of the learning data where it was")]
    public async Task SwitchingLearningLeavesEveryRowOfTheLearningData()
    {
        await ClearAsync();

        foreach (int recorded in Enumerable.Range(0, 3))
        {
            RecordingId recording = RecordingId.New();
            await AddAsync(LearningExtraction.Following(recording, Copy(Noon.AddHours(recorded)), ExtractionVersion.Current, Noon));
            await KeepAsync(Block(recording, LearningDataKind.SoundFingerprints, 0));
            await KeepAsync(Block(recording, LearningDataKind.Loudness, 0));
        }

        (long Extractions, long Blocks) before = await CountedAsync();

        Assert.True(before.Extractions >= 3);
        Assert.True(before.Blocks >= 6);

        await SaveAsync(SegmentSettings.LearningSwitched(true, Noon));
        Assert.Equal(before, await CountedAsync());

        await SaveAsync(SegmentSettings.LearningSwitched(false, Noon.AddMinutes(1)));
        Assert.Equal(before, await CountedAsync());

        await SaveAsync(SegmentSettings.LearningSwitched(true, Noon.AddMinutes(2)));
        Assert.Equal(before, await CountedAsync());
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

    private static LearningDataBlock Block(RecordingId recording, LearningDataKind kind, int chunk)
    {
        LearningDataChunk sample = LearningDataChunk.Of(
            chunk,
            FrameClock.Of(30000, 1001, LearningData.ChunkStarts(chunk)),
            [1u, 2u, 3u],
            [10, 20, 30],
            [],
            [],
            []);

        return LearningDataBlock.Of(recording, LearningDataPart.Of(sample, kind), ExtractionVersion.Current, Noon);
    }

    private async Task<(long Extractions, long Blocks)> CountedAsync()
    {
        await using CarinaDbContext counting = database.Open();

        return (
            await counting.Set<LearningExtraction>().LongCountAsync(Cancel),
            await counting.Set<LearningDataBlock>().LongCountAsync(Cancel));
    }

    private async Task AddAsync(LearningExtraction extraction)
    {
        await using CarinaDbContext writing = database.Open();
        await new LearningExtractionRepository(writing).AddAsync(extraction, Cancel);
    }

    private async Task KeepAsync(LearningDataBlock block)
    {
        await using CarinaDbContext writing = database.Open();
        await new LearningDataRepository(writing).KeepAsync(block, Cancel);
    }

    private async Task SaveAsync(SegmentSettings settings)
    {
        await using CarinaDbContext writing = database.Open();
        await new SegmentSettingsRepository(writing).SaveAsync(settings, Cancel);
    }

    private async Task RunAsync(string sql)
    {
        await using CarinaDbContext running = database.Open();
        await running.Database.ExecuteSqlRawAsync(sql, Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<SegmentSettings>().ExecuteDeleteAsync(Cancel);
    }
}
