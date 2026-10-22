using System.Runtime.InteropServices;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Segments;

/// <summary>
/// The catalog held in memory: entries and stretches as they were kept, a stretch given its identity as it
/// is kept.
/// </summary>
internal sealed class HeldCatalog : ICatalogRepository
{
    private long identity;

    public List<CatalogEntry> Entries { get; } = [];

    public List<CatalogOccurrence> Occurrences { get; } = [];

    public int Keeps { get; private set; }

    public Task<IReadOnlyList<CatalogEntry>> ListEntriesAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CatalogEntry>>([.. Entries]);

    public Task<IReadOnlyList<CatalogEntry>> EntriesAsync(IReadOnlyCollection<CatalogEntryId> entries, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CatalogEntry>>([.. Entries.Where(entry => entries.Contains(entry.Id))]);

    public Task<IReadOnlyList<CatalogOccurrence>> OccurrencesOfAsync(IReadOnlyCollection<CatalogEntryId> entries, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CatalogOccurrence>>([.. Occurrences.Where(occurrence => entries.Contains(occurrence.EntryId))]);

    public Task<IReadOnlyList<CatalogOccurrence>> OccurrencesInAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<CatalogOccurrence>>([.. Occurrences.Where(occurrence => recordings.Contains(occurrence.RecordingId))]);

    public Task<CatalogSurroundings> SurroundingsAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken)
        => Task.FromResult(new CatalogSurroundings(
        [
            .. Occurrences
                .Where(occurrence => recordings.Contains(occurrence.RecordingId))
                .Join(Entries, occurrence => occurrence.EntryId, entry => entry.Id, (occurrence, entry) => new ThemeStretch(
                    entry.Id,
                    occurrence.RecordingId,
                    occurrence.Span,
                    CatalogKinds.PartOf(new CatalogEstimate(entry.Kind, entry.Confidence, entry.DecidedBy), entry.SeriesCount, occurrence.KindOverride))),
        ]));

    public Task<IReadOnlyList<IReadOnlyList<RecordingId>>> SoundSharersAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<IReadOnlyList<RecordingId>>>(
        [
            .. Entries
                .Select(entry => (IReadOnlyList<RecordingId>)
                [
                    .. Occurrences
                        .Where(occurrence => occurrence.EntryId.Equals(entry.Id) && CatalogKinds.LinksSeries(entry.Kind, entry.Length, occurrence))
                        .Select(occurrence => occurrence.RecordingId)
                        .Distinct(),
                ])
                .Where(group => group.Count > 0),
        ]);

    public Task<CatalogEntry?> NextToReestimateAsync(CancellationToken cancellationToken)
        => Task.FromResult(Entries.Where(entry => entry.AwaitsReestimation).OrderBy(entry => entry.FirstSeenAt).ThenBy(entry => entry.Id.Value).FirstOrDefault());

    public Task KeepAsync(CatalogChange change, CancellationToken cancellationToken)
    {
        Entries.AddRange(change.Started);
        Occurrences.AddRange(change.Found.Select(Kept));
        Keeps++;

        return Task.CompletedTask;
    }

    private CatalogOccurrence Kept(CatalogOccurrence found)
        => CatalogOccurrence.Rehydrate(
            ++identity,
            found.EntryId,
            found.RecordingId,
            found.StartsAt,
            found.EndsAt,
            found.NetworkId,
            found.ServiceId,
            found.SeriesName,
            found.RecordingStartedAt,
            found.BitErrorRate,
            found.KindOverride,
            found.MatchedAt);
}

/// <summary>
/// The records and learning data held in memory, read for matching the way the store reads them.
/// </summary>
internal sealed class HeldCatalogWorklist(HeldLearningExtractions records, HeldLearningData data) : ICatalogWorklist
{
    public int Asked { get; private set; }

    public List<RecordingId> Heard { get; } = [];

    public Action<RecordingId>? WhileHearing { get; set; }

    public Task<LearningExtraction?> NextAsync(CancellationToken cancellationToken)
    {
        Asked++;

        return Task.FromResult(records.All()
            .Where(record => record.AwaitsMatching)
            .OrderByDescending(record => record.Programme.RecordingStartedAt)
            .FirstOrDefault());
    }

    public Task<IReadOnlyList<LearningExtraction>> AroundAsync(RecordingId heard, DateTime startedAt, TimeSpan within, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LearningExtraction>>(
        [
            .. records.All().Where(record => record.State is LearningExtractionState.Done or LearningExtractionState.Partial
                                             && !record.RecordingId.Equals(heard)
                                             && (record.Programme.RecordingStartedAt - startedAt).Duration() <= within),
        ]);

    public Task<IReadOnlyDictionary<RecordingId, ProgrammeCopy>> ProgrammesAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<RecordingId, ProgrammeCopy>>(
            records.All().Where(record => recordings.Contains(record.RecordingId)).ToDictionary(record => record.RecordingId, record => record.Programme));

    public Task<SoundTrack> SoundAsync(RecordingId recording, CancellationToken cancellationToken)
    {
        Heard.Add(recording);
        WhileHearing?.Invoke(recording);

        return Task.FromResult(SoundTrack.Of(data.Of(recording).Select(Part).OfType<LearningDataPart>()));
    }

    private static LearningDataPart? Part(LearningDataBlock block) => block.TryRead(out LearningDataPart? part) ? part : null;
}

internal sealed class CatalogMatchingHarness : IDisposable
{
    public const int Minutes = 5;

    public static readonly DateTime Now = new(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc);

    private const int Frames = Minutes * 60 * 1000 / 16;

    private readonly ServiceProvider provider;

    public CatalogMatchingHarness()
    {
        Worklist = new HeldCatalogWorklist(Records, Data);
        Busyness = new HeldBusyness(() => new Busyness(Recording, false, null));
        provider = new ServiceCollection()
            .AddSingleton<ILearningExtractionRepository>(Records)
            .AddSingleton<IBusynessReader>(Busyness)
            .AddScoped<IOccupancyReader, OccupancyReader>()
            .AddSingleton<ICatalogWorklist>(Worklist)
            .AddSingleton<ICatalogRepository>(Catalog)
            .AddSingleton<IRecordingDetectionRepository>(Detections)
            .AddSingleton<ISegmentModelVersionRepository>(Versions)
            .AddSingleton(Lowest)
            .AddScoped<CatalogMatching>()
            .BuildServiceProvider();

        Store = new LearningRecords(provider.GetRequiredService<IServiceScopeFactory>());
    }

    public HeldLearningExtractions Records { get; } = new();

    public HeldLearningData Data { get; } = new();

    public HeldBusyness Busyness { get; }

    public HeldCatalogWorklist Worklist { get; }

    public HeldCatalog Catalog { get; } = new();

    public HeldRecordingDetections Detections { get; } = new();

    public HeldSegmentModelVersions Versions { get; } = new();

    public LearningRecords Store { get; }

    public LowestPriorityScheduler Lowest { get; } = new();

    public FollowedToTheEnd Followed { get; } = new();

    public MatchedAtOnce MatchedAtOnce { get; } = new();

    public bool Recording { get; set; }

    public CatalogMatchingJob Job(CatalogMatchingSettings? settings = null, DateTime? at = null)
        => new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Store,
            Followed,
            MatchedAtOnce,
            settings ?? CatalogMatchingSettings.Default,
            new FixedClock(at ?? Now),
            NullLogger<CatalogMatchingJob>.Instance);

    /// <summary>
    /// Fingerprints that sound like nothing else, of a recording <see cref="Minutes"/> long.
    /// </summary>
    public static uint[] Sound(int seed)
    {
        Random random = new(seed);
        uint[] fingerprints = new uint[Frames];

        random.NextBytes(MemoryMarshal.AsBytes(fingerprints.AsSpan()));

        return fingerprints;
    }

    public static uint[] Sharing(uint[] sound, uint[] from, int atFrame, int fromFrame, int frames)
    {
        uint[] shared = [.. sound];

        from.AsSpan(fromFrame, frames).CopyTo(shared.AsSpan(atFrame, frames));

        return shared;
    }

    /// <summary>
    /// The record of a recording of a weekly series, read to its end <paramref name="updatedMinutesAgo"/>
    /// minutes ago, with its fingerprints kept.
    /// </summary>
    public async Task<RecordingId> ReadAsync(uint[] fingerprints, int daysAgo, string series, int station = 1, int updatedMinutesAgo = 10)
    {
        RecordingId id = RecordingId.New();
        DateTime started = Now.AddDays(-daysAgo).AddMinutes(-Minutes - updatedMinutesAgo);
        ProgrammeCopy programme = new(
            new NetworkId(station),
            new ServiceId(station),
            started.AddSeconds(30),
            started.AddMinutes(Minutes),
            started,
            $"{series} #{daysAgo}",
            [],
            [],
            null,
            AudioMode.Stereo,
            series);
        LearningExtraction record = LearningExtraction.Waiting(id, programme, started);

        record.Read(ExtractionVersion.Current, started);
        record.Reached(TimeSpan.FromMinutes(Minutes), started.AddMinutes(Minutes));
        record.Finish(Now.AddMinutes(-updatedMinutesAgo));
        Records.Hold(record);

        LearningDataChunk chunk = LearningDataChunk.Of(
            0,
            FrameClock.Of(30000, 1001, TimeSpan.Zero),
            fingerprints,
            [.. Enumerable.Repeat((byte)60, Minutes * 60 * 50)],
            [],
            [],
            []);

        foreach (LearningDataKind kind in new[] { LearningDataKind.SoundFingerprints, LearningDataKind.Loudness })
        {
            await Data.KeepAsync(LearningDataBlock.Of(id, LearningDataPart.Of(chunk, kind), ExtractionVersion.Current, started), CancellationToken.None);
        }

        return id;
    }

    public void Dispose()
    {
        provider.Dispose();
        Lowest.Dispose();
    }

    private sealed class FixedClock(DateTime at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(at);
    }
}
