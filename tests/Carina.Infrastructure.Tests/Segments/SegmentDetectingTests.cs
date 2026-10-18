using System.Runtime.InteropServices;

using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Segments.SegmentDetectionHarness;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class SegmentDetectingTests
{
    private static readonly uint[] Fifteen = [.. Enumerable.Range(1, 938).Select(frame => (uint)frame * 2_654_435_761u)];

    [Fact(DisplayName = "a detection reads the recording's readings as far as its data was read")]
    public async Task ADetectionReadsTheRecordingsReadings()
    {
        using SegmentDetectionHarness harness = new();
        RecordingId recording = await harness.ReadAsync(0, "架空の物語", cuts: [100]);

        DetectionInputs inputs = await GatherAsync(harness, recording);

        Assert.Equal(TimeSpan.FromSeconds(Seconds), inputs.Evidence.Length);
        Assert.Equal(Seconds * FramesASecond, inputs.Evidence.Frames.Length);
        Assert.Contains(SoundLoudness.Silent, inputs.Evidence.Loudness.ToArray());
        Assert.Equal(Now, inputs.CatalogUntil);
        Assert.Equal(TimeSpan.Zero, inputs.Context.ProgrammeStarts);
    }

    [Fact(DisplayName = "the catalog's stretches heard in the recording are read with their entry's kind, or the kind the stretch alone is taken for")]
    public async Task TheCatalogsStretchesAreReadWithTheirKinds()
    {
        using SegmentDetectionHarness harness = new();
        RecordingId recording = await harness.ReadAsync(0, "架空の物語");
        RecordingId elsewhere = await harness.ReadAsync(3, "架空のクイズ");
        ProgrammeCopy programme = harness.Records.Row(recording)!.Programme;
        CatalogEntryId id = CatalogEntryId.New();
        CatalogOccurrence[] heard =
        [
            CatalogOccurrence.Found(id, recording, new SoundSpan(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(115)), programme, 0.05, Now),
            CatalogOccurrence.Found(id, recording, new SoundSpan(TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(215)), programme, 0.05, Now),
            CatalogOccurrence.Found(id, elsewhere, new SoundSpan(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(65)), harness.Records.Row(elsewhere)!.Programme, 0.05, Now),
        ];
        harness.Catalog.Entries.Add(CatalogEntry.Start(id, Fifteen, [.. heard.Select(occurrence => PlacedOccurrence.Of(occurrence, null, null))], SeriesLinks.None, Now));
        harness.Catalog.Occurrences.AddRange([heard[0], Overridden(heard[1], SegmentKind.Programme), heard[2]]);

        DetectionInputs inputs = await GatherAsync(harness, recording);

        Assert.Equal(CatalogKind.Cm, harness.Catalog.Entries[0].Kind);
        Assert.Equal(
            [(100d, SegmentKind.Break), (200d, SegmentKind.Programme)],
            inputs.Context.Catalog.Select(stretch => (stretch.Starts.TotalSeconds, stretch.Kind!.Value)));
    }

    [Fact(DisplayName = "BR-CD-012: the station's logo is learned from its earlier recordings, never from later ones, other stations' or the recording itself")]
    public async Task TheLogoIsLearnedFromTheStationsEarlierRecordings()
    {
        using SegmentDetectionHarness harness = new();
        RecordingId judged = await harness.ReadAsync(3, "架空の物語", logo: true);
        await harness.ReadAsync(1, "架空のクイズ", logo: true);
        await harness.ReadAsync(0, "架空の天気", logo: true);
        await harness.ReadAsync(5, "架空の物語", station: 2, logo: true);
        await harness.ReadAsync(6, "架空の物語", station: 2, logo: true);

        DetectionInputs alone = await GatherAsync(harness, judged);

        await harness.ReadAsync(4, "架空の天気", logo: true);
        await harness.ReadAsync(5, "架空のクイズ", logo: true);
        DetectionInputs learned = await GatherAsync(harness, judged);

        Assert.Empty(alone.Context.Logo.Shapes);
        Assert.Equal(8, Assert.Single(learned.Context.Logo.Shapes).Count);
    }

    [Fact(DisplayName = "the earlier episodes are what the version in use detected of the series' earlier recordings, leaving out what it threw away, other series and later episodes")]
    public async Task TheEarlierEpisodesAreWhatTheVersionInUseDetected()
    {
        using SegmentDetectionHarness harness = new();
        RecordingId judged = await harness.ReadAsync(0, "架空の物語");
        RecordingId lastWeek = await harness.ReadAsync(7, "架空の物語");
        RecordingId twoWeeksAgo = await harness.ReadAsync(14, "架空の物語");
        RecordingId thrownAway = await harness.ReadAsync(21, "架空の物語");
        RecordingId otherSeries = await harness.ReadAsync(7, "架空のクイズ", station: 2);
        RecordingId byAnotherVersion = await harness.ReadAsync(28, "架空の物語");
        await Detected(harness, lastWeek, SegmentModelVersion.FirstNumber, (0, 100, SegmentKind.Programme), (100, 160, SegmentKind.Break), (160, 300, SegmentKind.Programme));
        await Detected(harness, twoWeeksAgo, SegmentModelVersion.FirstNumber, (0, 300, SegmentKind.Programme));
        await Detected(harness, thrownAway, SegmentModelVersion.FirstNumber, (0, 100, SegmentKind.Programme), (100, 300, SegmentKind.Break));
        await Detected(harness, otherSeries, SegmentModelVersion.FirstNumber, (0, 100, SegmentKind.Programme), (100, 160, SegmentKind.Break), (160, 300, SegmentKind.Programme));
        await Detected(harness, byAnotherVersion, 2, (0, 100, SegmentKind.Programme), (100, 160, SegmentKind.Break), (160, 300, SegmentKind.Programme));
        await Detected(harness, judged, SegmentModelVersion.FirstNumber, (0, 300, SegmentKind.Programme));

        DetectionInputs inputs = await GatherAsync(harness, lastWeek);

        PastEpisode episode = Assert.Single(inputs.Context.Past);
        Assert.Equal(TimeSpan.Zero, episode.ProgrammeStarts);
        Assert.Single(episode.Segments);
        Assert.Equal("架空の物語", inputs.Context.Series);
    }

    [Fact(DisplayName = "an earlier recording on another station sharing the sound of a programme fixture with the recording is an earlier episode of its series")]
    public async Task ARecordingSharingAFixtureOnAnotherStationIsAnEarlierEpisode()
    {
        using SegmentDetectionHarness harness = new();
        RecordingId judged = await harness.ReadAsync(0, "架空の物語");
        RecordingId delayed = await harness.ReadAsync(7, "まるで違う架空の名前", station: 2);
        await Detected(harness, delayed, SegmentModelVersion.FirstNumber, (0, 100, SegmentKind.Programme), (100, 160, SegmentKind.Break), (160, 300, SegmentKind.Programme));
        CatalogEntryId id = CatalogEntryId.New();
        CatalogOccurrence[] heard =
        [
            CatalogOccurrence.Found(id, judged, new SoundSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40)), harness.Records.Row(judged)!.Programme, 0.05, Now),
            CatalogOccurrence.Found(id, delayed, new SoundSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40)), harness.Records.Row(delayed)!.Programme, 0.05, Now),
        ];
        harness.Catalog.Entries.Add(Fixture(id, heard));
        harness.Catalog.Occurrences.AddRange(heard);

        DetectionInputs inputs = await GatherAsync(harness, judged);

        Assert.Single(inputs.Context.Past);
    }

    private static async Task<DetectionInputs> GatherAsync(SegmentDetectionHarness harness, RecordingId recording)
        => await harness.Detecting().GatherAsync(harness.Records.Row(recording)!, SegmentModelVersion.FirstNumber, Now, CancellationToken.None);

    private static async Task Detected(SegmentDetectionHarness harness, RecordingId recording, int version, params (double Starts, double Ends, SegmentKind Kind)[] stretches)
    {
        SegmentSpan[] segments = [.. stretches.Select(stretch => new SegmentSpan(TimeSpan.FromSeconds(stretch.Starts), TimeSpan.FromSeconds(stretch.Ends), stretch.Kind, 0.9))];

        await harness.Detections.KeepAsync(
            RecordingDetection.Of(recording, version, SegmentDetection.Judged(segments, TimeSpan.FromSeconds(Seconds), []), 2, Now, Now),
            CancellationToken.None);
    }

    private static CatalogEntry Fixture(CatalogEntryId id, CatalogOccurrence[] heard)
        => CatalogEntry.Rehydrate(
            id,
            CatalogKind.ProgrammeFixture,
            0.9,
            CatalogDecision.Pattern,
            TimeSpan.FromSeconds(30),
            MemoryMarshal.AsBytes(Fifteen.AsSpan()).ToArray(),
            heard.Length,
            2,
            2,
            Now,
            Now,
            Now);

    private static CatalogOccurrence Overridden(CatalogOccurrence found, SegmentKind kind)
        => CatalogOccurrence.Rehydrate(
            1,
            found.EntryId,
            found.RecordingId,
            found.StartsAt,
            found.EndsAt,
            found.NetworkId,
            found.ServiceId,
            found.SeriesName,
            found.RecordingStartedAt,
            found.BitErrorRate,
            kind,
            found.MatchedAt);
}
