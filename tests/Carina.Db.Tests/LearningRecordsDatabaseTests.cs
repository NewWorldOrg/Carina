using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Segments;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class LearningRecordsDatabaseTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Noon = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "a change that meets a row another writer moved after it was read is made again on the row as it stands")]
    public async Task AChangeThatMeetsAMovedRowIsMadeAgain()
    {
        await using ServiceProvider provider = Provider();
        LearningRecords records = new(provider.GetRequiredService<IServiceScopeFactory>());
        RecordingId recording = RecordingId.New();
        LearningDataGap gap = new(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(30));
        int attempts = 0;

        await records.AddAsync(LearningExtraction.Following(recording, Copy(), ExtractionVersion.Current, Noon), Cancel);

        ExtractionChange change = await records.ChangeAsync(
            recording,
            record =>
            {
                if (++attempts is 1)
                {
                    MovedElsewhere(recording, gap);
                }

                record.Reached(TimeSpan.FromSeconds(600), Noon.AddMinutes(10));

                return true;
            },
            Cancel);

        LearningExtraction? kept = await records.FindAsync(recording, Cancel);

        Assert.Equal(ExtractionChange.Written, change);
        Assert.Equal(2, attempts);
        Assert.NotNull(kept);
        Assert.Equal(TimeSpan.FromSeconds(600), kept.ReadThrough);
        Assert.Equal([gap], kept.Gaps);
    }

    [Fact(DisplayName = "a follow's chunks, its sound, its gaps and how far it read are kept, and it ends done")]
    public async Task AFollowsChunksAndProgressAreKeptAndItEndsDone()
    {
        await using ServiceProvider provider = Provider();
        LearningRecords records = new(provider.GetRequiredService<IServiceScopeFactory>());
        RecordingId recording = RecordingId.New();
        ExtractionSound sound = new(0, TimeSpan.FromMilliseconds(-33));
        LearningDataGap[] gaps =
        [
            new(TimeSpan.FromSeconds(12.03), TimeSpan.FromSeconds(30)),
            new(TimeSpan.FromSeconds(301), TimeSpan.FromSeconds(302.5)),
        ];

        await records.AddAsync(LearningExtraction.Following(recording, Copy(), ExtractionVersion.Current, Noon), Cancel);
        await records.KeepAsync(recording, [Chunk(0), Chunk(1)], ExtractionVersion.Current, Noon.AddMinutes(20), Cancel);
        await records.ChangeAsync(
            recording,
            record =>
            {
                record.Opened(sound, Noon.AddMinutes(20));
                record.Missed(gaps[0], Noon.AddMinutes(20));
                record.Missed(gaps[1], Noon.AddMinutes(20));
                record.Reached(TimeSpan.FromSeconds(1200), Noon.AddMinutes(20));

                return true;
            },
            Cancel);
        await records.ChangeAsync(
            recording,
            record =>
            {
                record.Reached(TimeSpan.FromSeconds(1234.5), Noon.AddMinutes(21));
                record.Finish(Noon.AddMinutes(21));

                return true;
            },
            Cancel);

        LearningExtraction? kept = await records.FindAsync(recording, Cancel);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        int rows = await scope.ServiceProvider.GetRequiredService<ILearningDataRepository>().CountAsync(recording, Cancel);

        Assert.NotNull(kept);
        Assert.Equal(
            (LearningExtractionState.Done, TimeSpan.FromSeconds(1234.5), sound),
            (kept.State, kept.ReadThrough, kept.Sound));
        Assert.Equal(gaps, kept.Gaps);
        Assert.Equal(2 * LearningDataChunk.Kinds.Count, rows);
    }

    private ServiceProvider Provider()
        => new ServiceCollection()
            .AddScoped(_ => CarinaDbContextFactory.Create(database.ConnectionString))
            .AddScoped<ILearningExtractionRepository, LearningExtractionRepository>()
            .AddScoped<ILearningDataRepository, LearningDataRepository>()
            .BuildServiceProvider();

    private void MovedElsewhere(RecordingId recording, LearningDataGap gap)
    {
        using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);
        LearningExtractionRepository elsewhere = new(context);
        LearningExtraction moved = elsewhere.FindAsync(recording, Cancel).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("The record that was just written is not there.");

        moved.Missed(gap, Noon.AddMinutes(5));
        elsewhere.SaveAsync(moved, Cancel).GetAwaiter().GetResult();
    }

    private static ProgrammeCopy Copy()
        => new(
            new NetworkId(40_002),
            new ServiceId(60_002),
            Noon.AddMinutes(-5),
            Noon.AddMinutes(25),
            Noon.AddMinutes(-6),
            "架空の番組",
            [new ProgrammeGenre(7, 0)],
            [],
            null,
            AudioMode.Stereo,
            null);

    private static LearningDataChunk Chunk(int index)
        => LearningDataChunk.Of(
            index,
            FrameClock.Of(30000, 1001, LearningData.ChunkStarts(index)),
            [1u, 2u, 3u],
            [40, 41, 42],
            [],
            [new FrameLight(90, 3)],
            []);
}
