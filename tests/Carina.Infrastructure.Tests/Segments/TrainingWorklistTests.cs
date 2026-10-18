using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Segments;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class TrainingWorklistTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Started = new(2033, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly uint[] Fifteen = [.. Enumerable.Range(1, 938).Select(frame => (uint)frame * 2_654_435_761u)];

    private readonly RecordingId confirmed = RecordingId.New();

    private readonly RecordingId answered = RecordingId.New();

    private readonly RecordingId heard = RecordingId.New();

    private readonly RecordingId skipped = RecordingId.New();

    private readonly RecordingId withdrawn = RecordingId.New();

    private readonly RecordingId nothing = RecordingId.New();

    private readonly RecordingId unread = RecordingId.New();

    private readonly CatalogEntryId entry = CatalogEntryId.New();

    private RecordingId[] Ours => [confirmed, answered, heard, skipped, withdrawn, nothing, unread];

    public Task InitializeAsync() => ClearAsync();

    public Task DisposeAsync() => ClearAsync();

    [Fact(DisplayName = "BR-CD-004: a round could read every recording read through with an answer standing, a stretch of the catalog or a skip event, by what it is learned by, and no other")]
    public async Task EveryRecordingWithSomethingToLearnItByIsACandidate()
    {
        foreach (RecordingId recording in Ours.Where(recording => !recording.Equals(unread)))
        {
            await KeepRecordAsync(recording, readThrough: true);
        }

        await KeepRecordAsync(unread, readThrough: false);
        await AnswerAsync(confirmed, true);
        await AnswerAsync(answered, false);
        await AnswerAsync(withdrawn, true);
        await AnswerAsync(unread, true);

        await using (CarinaDbContext writing = database.Open())
        {
            await new SegmentAnswerRepository(writing).WithdrawAsync(withdrawn, Started.AddDays(1), Cancel);
            await new SkipEventRepository(writing).TakeAsync(
                SkipEvent.Of(skipped, new ViewerAction(Started.AddDays(1), ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(115), null)),
                Cancel);
            CatalogOccurrence[] found = [Found(heard, 100), Found(heard, 200)];
            writing.Add(CatalogEntry.Start(entry, Fifteen, [.. found.Select(occurrence => PlacedOccurrence.Of(occurrence, null, null))], SeriesLinks.None, Started.AddDays(1)));
            writing.AddRange(found);
            await writing.SaveChangesAsync(Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<LearningCandidate> candidates = await new TrainingWorklist(reading).CandidatesAsync(Cancel);

        Assert.Equal(
            [(confirmed, LearningGround.Confirmed), (answered, LearningGround.Answered), (heard, LearningGround.Other), (skipped, LearningGround.Other)],
            candidates.Where(candidate => Ours.Contains(candidate.Recording)).OrderBy(candidate => Array.IndexOf(Ours, candidate.Recording)).Select(candidate => (candidate.Recording, candidate.Ground)));
        Assert.All(
            candidates.Where(candidate => Ours.Contains(candidate.Recording)),
            candidate => Assert.Equal((Started, TimeSpan.FromMinutes(5)), (candidate.StartedAt, candidate.ReadThrough)));
    }

    private CatalogOccurrence Found(RecordingId recording, double at)
        => CatalogOccurrence.Found(
            entry,
            recording,
            new SoundSpan(TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at + 15)),
            SegmentDetectionHarness.Copy("架空の物語", Started),
            0.05,
            Started.AddDays(1));

    private async Task AnswerAsync(RecordingId recording, bool sure)
    {
        await using CarinaDbContext writing = database.Open();
        await new SegmentAnswerRepository(writing).AnswerAsync(
            RecordingAnswer.Given(recording, AnsweredStretches.Read([new SentStretch(0, 300, SegmentKind.Programme)], TimeSpan.FromMinutes(5)).Stretches!, sure, Started.AddDays(1)),
            Cancel);
    }

    private async Task KeepRecordAsync(RecordingId recording, bool readThrough)
    {
        LearningExtraction record = LearningExtraction.Waiting(recording, SegmentDetectionHarness.Copy("架空の物語", Started), Started);

        if (readThrough)
        {
            record.Read(ExtractionVersion.Current, Started);
            record.Reached(TimeSpan.FromMinutes(5), Started.AddMinutes(5));
            record.Finish(Started.AddMinutes(5));
        }

        await using CarinaDbContext writing = database.Open();
        await new LearningExtractionRepository(writing).AddAsync(record, Cancel);
    }

    private async Task ClearAsync()
    {
        Guid[] recordings = [.. Ours.Select(recording => recording.Value)];
        Guid id = entry.Value;

        await using CarinaDbContext context = database.Open();
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_skip_event WHERE recording_id = ANY({recordings})", Cancel);
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_answer WHERE recording_id = ANY({recordings})", Cancel);
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_catalog_occurrence WHERE entry_id = {id}", Cancel);
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_catalog_entry WHERE id = {id}", Cancel);
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_extraction WHERE recording_id = ANY({recordings})", Cancel);
    }
}
