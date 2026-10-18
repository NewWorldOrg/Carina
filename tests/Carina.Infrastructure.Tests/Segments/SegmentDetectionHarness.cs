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
/// The records and learning data held in memory, read for detection with the filters and order the store reads
/// them with: a record waits to be detected as <see cref="RecordingDetection.IsAwaited"/> says, and only while its
/// recording is <see cref="Kept"/>.
/// </summary>
internal sealed class HeldDetectionWorklist(HeldLearningExtractions records, HeldLearningData data, HeldRecordingDetections detections) : IDetectionWorklist
{
    public HashSet<RecordingId> Kept { get; } = [];

    public HashSet<RecordingId> Unreadable { get; } = [];

    public int Asked { get; private set; }

    public List<RecordingId> Read { get; } = [];

    public Action<RecordingId>? WhileReading { get; set; }

    public Task<LearningExtraction?> NextAsync(int modelVersion, IReadOnlyCollection<RecordingId> passedOver, CancellationToken cancellationToken)
    {
        Asked++;

        return Task.FromResult(records.All()
            .Where(record => Kept.Contains(record.RecordingId)
                             && !passedOver.Contains(record.RecordingId)
                             && RecordingDetection.IsAwaited(record, detections.Of(record.RecordingId, modelVersion)))
            .OrderByDescending(record => record.Programme.RecordingStartedAt)
            .ThenBy(record => record.RecordingId.Value)
            .FirstOrDefault());
    }

    public Task<IReadOnlyList<LearningExtraction>> EarlierOnTheStationAsync(SeriesMember judged, int most, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LearningExtraction>>(
        [
            .. Readable()
                .Where(record => record.Programme.NetworkId.Equals(judged.NetworkId)
                                 && record.Programme.ServiceId.Equals(judged.ServiceId)
                                 && record.Programme.RecordingStartedAt < judged.StartedAt
                                 && !record.RecordingId.Equals(judged.RecordingId))
                .OrderByDescending(record => record.Programme.RecordingStartedAt)
                .ThenBy(record => record.RecordingId.Value)
                .Take(most),
        ]);

    public Task<IReadOnlyList<LearningExtraction>> RecordsAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LearningExtraction>>(
        [
            .. Readable()
                .Where(record => recordings.Contains(record.RecordingId))
                .OrderBy(record => record.Programme.RecordingStartedAt)
                .ThenBy(record => record.RecordingId.Value),
        ]);

    public Task<IReadOnlyList<LearningDataPart>> PartsAsync(RecordingId recording, IReadOnlyCollection<LearningDataKind> kinds, CancellationToken cancellationToken)
    {
        if (Unreadable.Contains(recording))
        {
            throw new InvalidDataException("The learning data of this recording cannot be read.");
        }

        Read.Add(recording);
        WhileReading?.Invoke(recording);

        return Task.FromResult<IReadOnlyList<LearningDataPart>>(
        [
            .. data.Of(recording)
                .Where(block => kinds.Contains(block.Kind))
                .OrderBy(block => block.Kind)
                .ThenBy(block => block.Chunk)
                .Select(Part)
                .OfType<LearningDataPart>(),
        ]);
    }

    private IEnumerable<LearningExtraction> Readable()
        => records.All().Where(record => record.State is LearningExtractionState.Done or LearningExtractionState.Partial);

    private static LearningDataPart? Part(LearningDataBlock block) => block.TryRead(out LearningDataPart? part) ? part : null;
}

internal sealed class SegmentDetectionHarness : IDisposable
{
    public const int Seconds = 300;

    public const int FramesASecond = 10;

    public const byte Loud = 40;

    public static readonly DateTime Now = new(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc);

    private readonly ServiceProvider provider;

    public SegmentDetectionHarness()
    {
        Worklist = new HeldDetectionWorklist(Records, Data, Detections);
        Busyness = new HeldBusyness(() => new Busyness(Recording, false, null));
        provider = new ServiceCollection()
            .AddSingleton<ILearningExtractionRepository>(Records)
            .AddSingleton<ISegmentSettingsRepository>(Settings)
            .AddSingleton<IBusynessReader>(Busyness)
            .AddScoped<ILearningSwitch, LearningSwitch>()
            .AddScoped<IOccupancyReader, OccupancyReader>()
            .AddSingleton<IDetectionWorklist>(Worklist)
            .AddSingleton<ICatalogRepository>(Catalog)
            .AddSingleton<ISegmentModelVersionRepository>(Versions)
            .AddSingleton<IRecordingDetectionRepository>(Detections)
            .AddSingleton(Lowest)
            .AddSingleton(SegmentDetectionSettings.Default)
            .AddScoped<SegmentDetecting>()
            .BuildServiceProvider();

        Store = new LearningRecords(provider.GetRequiredService<IServiceScopeFactory>());
    }

    public HeldLearningExtractions Records { get; } = new();

    public HeldLearningData Data { get; } = new();

    public HeldSegmentSettings Settings { get; } = new();

    public HeldBusyness Busyness { get; }

    public HeldDetectionWorklist Worklist { get; }

    public HeldCatalog Catalog { get; } = new();

    public HeldSegmentModelVersions Versions { get; } = new();

    public HeldRecordingDetections Detections { get; } = new();

    public LearningRecords Store { get; }

    public LowestPriorityScheduler Lowest { get; } = new();

    public MatchedAtOnce MatchedAtOnce { get; } = new();

    public bool Recording { get; set; }

    public static ProgrammeCopy Copy(string series, DateTime startedAt, int station = 1)
        => new(
            new NetworkId(station),
            new ServiceId(station),
            startedAt,
            startedAt.AddSeconds(Seconds),
            startedAt,
            $"{series} #1",
            [],
            [],
            1,
            AudioMode.Stereo,
            series);

    public SegmentDetectionJob Job(SegmentDetectionSettings? settings = null)
        => new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Store,
            MatchedAtOnce,
            settings ?? SegmentDetectionSettings.Default,
            new FixedClock(Now),
            NullLogger<SegmentDetectionJob>.Instance);

    public SegmentDetecting Detecting() => provider.CreateScope().ServiceProvider.GetRequiredService<SegmentDetecting>();

    public async Task LearningAsync(bool on) => await Settings.SaveAsync(SegmentSettings.LearningSwitched(on, Now), CancellationToken.None);

    public void LearningOff() => Settings.SaveAsync(SegmentSettings.LearningSwitched(false, Now), CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// The record of a recording <see cref="Seconds"/> long, still kept, read to its end and matched against the
    /// catalog ten minutes ago unless said otherwise: loud sound with a silent black cut at each of
    /// <paramref name="cuts"/>, and, when <paramref name="logo"/> says so, a logo steady in a corner.
    /// </summary>
    public async Task<RecordingId> ReadAsync(
        int daysAgo,
        string series,
        int station = 1,
        double[]? cuts = null,
        bool logo = false,
        bool matched = true)
    {
        RecordingId id = RecordingId.New();
        DateTime started = Now.AddDays(-daysAgo).AddMinutes(-20);
        LearningExtraction record = LearningExtraction.Waiting(id, Copy(series, started, station), started);

        record.Read(ExtractionVersion.Current, started);
        record.Reached(TimeSpan.FromSeconds(Seconds), started.AddSeconds(Seconds));
        record.Finish(Now.AddMinutes(-10));

        if (matched)
        {
            record.Matched(Now.AddMinutes(-10));
        }

        Records.Hold(record);
        Worklist.Kept.Add(id);

        LearningDataChunk chunk = LearningDataChunk.Of(
            0,
            FrameClock.Of(FramesASecond, 1, TimeSpan.Zero),
            [],
            Loudness(cuts ?? []),
            [],
            Frames(cuts ?? []),
            logo ? Logo() : []);

        foreach (LearningDataKind kind in logo
                     ? new[] { LearningDataKind.Loudness, LearningDataKind.FrameLights, LearningDataKind.CornerOutlines }
                     : [LearningDataKind.Loudness, LearningDataKind.FrameLights])
        {
            await Data.KeepAsync(LearningDataBlock.Of(id, LearningDataPart.Of(chunk, kind), ExtractionVersion.Current, started), CancellationToken.None);
        }

        return id;
    }

    /// <summary>
    /// Reads the recording's data again and matches it again a minute from now.
    /// </summary>
    public void ReadAgain(RecordingId id)
    {
        LearningExtraction record = Records.Row(id)!;
        ExtractionVersion newer = new(LearningData.ExtractionVersion + 1, ExtractionOrigin.RecordingFile);

        record.Outdate(newer, Now.AddMinutes(1));
        record.Read(newer, Now.AddMinutes(1));
        record.Reached(TimeSpan.FromSeconds(Seconds), Now.AddMinutes(1));
        record.Finish(Now.AddMinutes(1));
        record.Matched(Now.AddMinutes(1));
        Records.Hold(record);
    }

    public void Dispose()
    {
        provider.Dispose();
        Lowest.Dispose();
    }

    private static byte[] Loudness(double[] cuts)
    {
        int perSecond = SoundReader.SampleRate / SoundLoudness.Hop;
        byte[] loudness = [.. Enumerable.Repeat(Loud, Seconds * perSecond)];

        foreach (double cut in cuts)
        {
            int middle = (int)Math.Round(cut * perSecond);

            loudness.AsSpan(middle - (perSecond / 4), perSecond / 2).Fill(SoundLoudness.Silent);
        }

        return loudness;
    }

    private static FrameLight[] Frames(double[] cuts)
    {
        FrameLight[] frames = [.. Enumerable.Repeat(new FrameLight(128, 2), Seconds * FramesASecond)];

        foreach (double cut in cuts)
        {
            int middle = (int)Math.Round(cut * FramesASecond);

            frames.AsSpan(middle - 2, 4).Fill(new FrameLight(0, 2));
        }

        return frames;
    }

    private static byte[] Logo()
    {
        byte[] outlines = new byte[Seconds * CornerOutline.Bytes];

        for (int second = 0; second < Seconds; second++)
        {
            outlines[second * CornerOutline.Bytes] = 0xFF;
        }

        return outlines;
    }

    private sealed class FixedClock(DateTime at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(at);
    }
}
