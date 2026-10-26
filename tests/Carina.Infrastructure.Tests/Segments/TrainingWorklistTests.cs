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

    private readonly RecordingId advertised = RecordingId.New();

    private readonly RecordingId unknown = RecordingId.New();

    private readonly RecordingId skipped = RecordingId.New();

    private readonly RecordingId sought = RecordingId.New();

    private readonly RecordingId nothing = RecordingId.New();

    private readonly RecordingId unread = RecordingId.New();

    private readonly CatalogEntryId advert = CatalogEntryId.New();

    private readonly CatalogEntryId notKnown = CatalogEntryId.New();

    private RecordingId[] Ours => [advertised, unknown, skipped, sought, nothing, unread];

    public Task InitializeAsync() => ClearAsync();

    public Task DisposeAsync() => ClearAsync();

    [Fact(DisplayName = "BR-CD-004: a round could read every recording read through with a stretch of the catalog, a skip event or a seek, those the catalog hears an advert in first, and no other")]
    public async Task EveryRecordingWithSomethingToLearnItByIsACandidate()
    {
        foreach (RecordingId recording in Ours.Where(recording => !recording.Equals(unread)))
        {
            await KeepRecordAsync(recording, readThrough: true);
        }

        await KeepRecordAsync(unread, readThrough: false);

        await using (CarinaDbContext writing = database.Open())
        {
            await new SkipEventRepository(writing).TakeAsync(
                SkipEvent.Of(skipped, new ViewerAction(Started.AddDays(1), ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(115), null)),
                Cancel);
            await new SeekEventRepository(writing).TakeAsync(
                SeekEvent.Of(sought, new ViewerSeek(Started.AddDays(1), SeekAction.Forward, SeekMeans.Bar, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(190))),
                Cancel);
            CatalogOccurrence[] adverts = [Found(advert, advertised, 100), Found(advert, advertised, 200), Found(advert, unread, 100)];
            CatalogOccurrence[] unknowns = [Found(notKnown, unknown, 100), Found(notKnown, unknown, 300)];
            CatalogEntry heardTwice = CatalogEntry.Start(advert, Fifteen, [.. adverts.Take(2).Select(occurrence => PlacedOccurrence.Of(occurrence, null))], SeriesLinks.None, Started.AddDays(1));
            writing.Add(heardTwice);
            writing.Add(CatalogEntry.Rehydrate(notKnown, CatalogKind.Unknown, null, null, TimeSpan.FromSeconds(15), CatalogFingerprint.Write(Fifteen), 2, 1, 1, Started, Started, Started.AddDays(1), CatalogKinds.Version));
            writing.AddRange([.. adverts, .. unknowns]);
            await writing.SaveChangesAsync(Cancel);

            Assert.Equal(CatalogKind.Cm, heardTwice.Kind);
        }

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<LearningCandidate> candidates = await new TrainingWorklist(reading).CandidatesAsync(Cancel);

        Assert.Equal(
            [(advertised, LearningGround.CatalogAdverts), (unknown, LearningGround.Other), (skipped, LearningGround.Other), (sought, LearningGround.Other)],
            candidates.Where(candidate => Ours.Contains(candidate.Recording)).OrderBy(candidate => Array.IndexOf(Ours, candidate.Recording)).Select(candidate => (candidate.Recording, candidate.Ground)));
        Assert.All(
            candidates.Where(candidate => Ours.Contains(candidate.Recording)),
            candidate => Assert.Equal((Started, TimeSpan.FromMinutes(5)), (candidate.StartedAt, candidate.ReadThrough)));
    }

    private static CatalogOccurrence Found(CatalogEntryId entry, RecordingId recording, double at)
        => CatalogOccurrence.Found(
            entry,
            recording,
            new SoundSpan(TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at + 15)),
            SegmentDetectionHarness.Copy("架空の物語", Started),
            0.05,
            Started.AddDays(1));

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
        Guid[] entries = [advert.Value, notKnown.Value];

        await using CarinaDbContext context = database.Open();
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_skip_event WHERE recording_id = ANY({recordings})", Cancel);
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_seek_event WHERE recording_id = ANY({recordings})", Cancel);
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_catalog_occurrence WHERE entry_id = ANY({entries})", Cancel);
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_catalog_entry WHERE id = ANY({entries})", Cancel);
        await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM segment_extraction WHERE recording_id = ANY({recordings})", Cancel);
    }
}
