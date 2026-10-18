using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Segments;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class LearningMaterialChangesTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Later = new(2034, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly uint[] Fifteen = [.. Enumerable.Range(1, 938).Select(frame => (uint)frame * 2_654_435_761u)];

    private readonly RecordingId answered = RecordingId.New();

    private readonly RecordingId heard = RecordingId.New();

    private readonly RecordingId elsewhere = RecordingId.New();

    private readonly RecordingId read = RecordingId.New();

    private readonly RecordingId partway = RecordingId.New();

    private readonly CatalogEntryId entry = CatalogEntryId.New();

    public Task InitializeAsync() => ClearAsync();

    public Task DisposeAsync() => ClearAsync();

    [Fact(DisplayName = "BR-CD-004: what learning reads last changed when an answer was sent or withdrawn, an entry of the catalog or a stretch of one kept, a skip event taken, or a recording's data read to its end")]
    public async Task EachKindOfChangeMovesTheLastChange()
    {
        DateTime? before = await LastChangedAsync();

        await using (CarinaDbContext writing = database.Open())
        {
            await new SegmentAnswerRepository(writing).AnswerAsync(
                RecordingAnswer.Given(answered, AnsweredStretches.Read([new SentStretch(0, 600, SegmentKind.Programme)], TimeSpan.FromMinutes(10)).Stretches!, true, Later.AddHours(1)),
                Cancel);
        }

        DateTime? sent = await LastChangedAsync();
        await using (CarinaDbContext writing = database.Open())
        {
            await new SegmentAnswerRepository(writing).WithdrawAsync(answered, Later.AddHours(2), Cancel);
        }

        DateTime? withdrawn = await LastChangedAsync();
        await KeepCatalogAsync(estimated: Later.AddHours(4), matched: Later.AddHours(3));
        DateTime? estimated = await LastChangedAsync();
        await KeepStretchAsync(heard, 300, Later.AddHours(5));
        DateTime? matched = await LastChangedAsync();
        await using (CarinaDbContext writing = database.Open())
        {
            await new SkipEventRepository(writing).TakeAsync(
                SkipEvent.Of(answered, new ViewerAction(Later.AddHours(6), ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(115), null)),
                Cancel);
        }

        DateTime? skipped = await LastChangedAsync();
        await KeepRecordAsync(partway, Later.AddHours(8), done: false);
        DateTime? notToTheEnd = await LastChangedAsync();
        await KeepRecordAsync(read, Later.AddHours(7), done: true);
        DateTime? readToTheEnd = await LastChangedAsync();

        Assert.True(before is null || before < Later);
        Assert.Equal(
            [Later.AddHours(1), Later.AddHours(2), Later.AddHours(4), Later.AddHours(5), Later.AddHours(6), Later.AddHours(6), Later.AddHours(7)],
            new[] { sent, withdrawn, estimated, matched, skipped, notToTheEnd, readToTheEnd });
    }

    private async Task<DateTime?> LastChangedAsync()
    {
        await using CarinaDbContext reading = database.Open();

        return await new LearningMaterialChanges(reading).LastChangedAsync(Cancel);
    }

    private async Task KeepCatalogAsync(DateTime estimated, DateTime matched)
    {
        CatalogOccurrence[] found = [Found(heard, 100, matched), Found(elsewhere, 50, matched)];

        await using CarinaDbContext writing = database.Open();
        writing.Add(CatalogEntry.Start(entry, Fifteen, [.. found.Select(occurrence => PlacedOccurrence.Of(occurrence, null, null))], SeriesLinks.None, estimated));
        writing.AddRange(found);
        await writing.SaveChangesAsync(Cancel);
    }

    private async Task KeepStretchAsync(RecordingId recording, double at, DateTime matched)
    {
        await using CarinaDbContext writing = database.Open();
        writing.Add(Found(recording, at, matched));
        await writing.SaveChangesAsync(Cancel);
    }

    private CatalogOccurrence Found(RecordingId recording, double at, DateTime matched)
        => CatalogOccurrence.Found(
            entry,
            recording,
            new SoundSpan(TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at + 15)),
            SegmentDetectionHarness.Copy("架空の物語", Later.AddDays(-1)),
            0.05,
            matched);

    private async Task KeepRecordAsync(RecordingId recording, DateTime finished, bool done)
    {
        DateTime started = Later.AddDays(-1);
        LearningExtraction record = LearningExtraction.Waiting(recording, SegmentDetectionHarness.Copy("架空の物語", started), started);

        record.Read(ExtractionVersion.Current, started);
        record.Reached(TimeSpan.FromMinutes(5), started.AddMinutes(5));

        if (done)
        {
            record.Finish(finished);
        }
        else
        {
            record.FinishPartway(finished);
        }

        await using CarinaDbContext writing = database.Open();
        await new LearningExtractionRepository(writing).AddAsync(record, Cancel);
    }

    private async Task ClearAsync()
    {
        Guid[] recordings = [answered.Value, heard.Value, elsewhere.Value, read.Value, partway.Value];
        Guid id = entry.Value;

        await using CarinaDbContext context = database.Open();
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_skip_event WHERE recording_id = ANY({recordings})", Cancel);
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_answer WHERE recording_id = ANY({recordings})", Cancel);
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_catalog_occurrence WHERE entry_id = {id}", Cancel);
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_catalog_entry WHERE id = {id}", Cancel);
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_extraction WHERE recording_id = ANY({recordings})", Cancel);
    }
}
