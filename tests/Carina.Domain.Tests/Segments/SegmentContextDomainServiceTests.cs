using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentContextDomainServiceTests
{
    private const string Series = "架空の番組";

    private const int Version = 4;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    private readonly CatalogShelf catalog = new();

    private readonly Readings worklist = new();

    private readonly HeldRecordingDetections detections = new();

    private SegmentContextDomainService Service => new(worklist, catalog, detections);

    [Fact(DisplayName = "BR-CD-007: the earlier episodes are the series' earlier recordings on the station as the same version detected them, leaving out one it threw away, one it did not detect and another series")]
    public async Task TheEarlierEpisodesAreTheSeriesAsDetected()
    {
        LearningExtraction judged = Record(Series, 0);
        LearningExtraction detected = Record(Series, 7);
        LearningExtraction thrownAway = Record(Series, 14);
        LearningExtraction undetected = Record(Series, 21);
        LearningExtraction another = Record("夜のニュース", 8);
        worklist.Earlier.AddRange([detected, thrownAway, undetected, another]);
        await DetectedAsync(detected, SegmentDetectionState.NothingFound);
        await DetectedAsync(thrownAway, SegmentDetectionState.Discarded);
        await DetectedAsync(another, SegmentDetectionState.NothingFound);
        await DetectedAsync(undetected, SegmentDetectionState.NothingFound, Version + 1);

        SegmentSurroundings known = await Service.GatherAsync(judged, Version, 10, 3, Cancel);

        Assert.Equal([detected.RecordingId], known.Past.Select(past => past.Recording));
        Assert.Equal(10, worklist.AskedMost);
    }

    [Fact(DisplayName = "BR-CD-011: an earlier recording on another station sharing a sound with the recording is an earlier episode, and a later one is not")]
    public async Task AnEarlierRecordingSharingASoundIsAnEpisode()
    {
        LearningExtraction judged = Record(Series, 0);
        LearningExtraction earlier = Record("別の局の番組", 7, service: 60_002);
        LearningExtraction later = Record("別の局の番組", -1, service: 60_002);
        worklist.Held.AddRange([earlier, later]);
        catalog.Sharers.Add([judged.RecordingId, earlier.RecordingId, later.RecordingId]);
        await DetectedAsync(earlier, SegmentDetectionState.NothingFound);
        await DetectedAsync(later, SegmentDetectionState.NothingFound);

        SegmentSurroundings known = await Service.GatherAsync(judged, Version, 10, 3, Cancel);

        Assert.Equal([earlier.RecordingId], known.Past.Select(past => past.Recording));
    }

    [Fact(DisplayName = "BR-CD-012: the station's logo is learned from the most recent earlier recordings on the station, as many as asked, that read something")]
    public async Task TheLogoIsLearnedFromTheMostRecentRecordingsRead()
    {
        LearningExtraction unread = Record(Series, 1, readThrough: TimeSpan.Zero);
        LearningExtraction[] read = [Record(Series, 2), Record(Series, 3), Record(Series, 4)];
        worklist.Earlier.AddRange([unread, .. read]);

        SegmentSurroundings known = await Service.GatherAsync(Record(Series, 0), Version, 10, 3, Cancel);

        Assert.Equal([read[0].RecordingId, read[1].RecordingId], worklist.AskedOutlines);
        Assert.Empty(known.Corners);
    }

    [Fact(DisplayName = "BR-CD-007: the catalog's stretches heard in the recording are read with their entry, and a stretch whose entry is gone is not")]
    public async Task TheStretchesHeardAreReadWithTheirEntry()
    {
        LearningExtraction judged = Record(Series, 0);
        CatalogEntry entry = CatalogEntry.Rehydrate(
            CatalogEntryId.New(), CatalogKind.Cm, 0.9, CatalogDecision.Pattern, TimeSpan.FromSeconds(15), CatalogFingerprint.Write([1, 2, 3, 4]), 2, 2, 1, Now.AddDays(-7), Now, Now, CatalogKinds.Version);
        catalog.Entries.Add(entry);
        catalog.Occurrences.AddRange(
        [
            CatalogOccurrence.Found(entry.Id, judged.RecordingId, new SoundSpan(TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(615)), judged.Programme, 0.05, Now),
            CatalogOccurrence.Found(CatalogEntryId.New(), judged.RecordingId, new SoundSpan(TimeSpan.FromSeconds(900), TimeSpan.FromSeconds(915)), judged.Programme, 0.05, Now),
        ]);

        SegmentSurroundings known = await Service.GatherAsync(judged, Version, 10, 3, Cancel);

        Assert.Single(known.Heard);
    }

    private static LearningExtraction Record(string name, int daysAgo, int service = 60_001, TimeSpan? readThrough = null)
    {
        DateTime starts = Now.AddDays(-daysAgo);
        ProgrammeCopy programme = new(
            new NetworkId(40_001),
            new ServiceId(service),
            starts,
            starts.AddMinutes(30),
            starts.AddMinutes(-1),
            name,
            [new ProgrammeGenre(7, 0)],
            [],
            null,
            AudioMode.Stereo,
            null);

        return LearningExtraction.Rehydrate(
            RecordingId.New(),
            LearningExtractionState.Done,
            ExtractionVersion.Current,
            readThrough ?? TimeSpan.FromMinutes(30),
            [],
            null,
            null,
            0,
            programme,
            starts,
            starts.AddMinutes(31));
    }

    private async Task DetectedAsync(LearningExtraction record, SegmentDetectionState state, int version = Version)
        => await detections.KeepAsync(
            RecordingDetection.Rehydrate(
                record.RecordingId,
                version,
                state,
                [new SegmentSpan(TimeSpan.Zero, TimeSpan.FromMinutes(30), SegmentKind.Programme, 0.9)],
                0,
                [],
                ExtractionVersion.Current.Number,
                Now,
                Now),
            Cancel);

    private sealed class Readings : IDetectionWorklist
    {
        public List<LearningExtraction> Earlier { get; } = [];

        public List<LearningExtraction> Held { get; } = [];

        public List<RecordingId> AskedOutlines { get; } = [];

        public int? AskedMost { get; private set; }

        public Task<LearningExtraction?> NextAsync(int modelVersion, IReadOnlyCollection<RecordingId> passedOver, CancellationToken cancellationToken)
            => Task.FromResult<LearningExtraction?>(null);

        public Task<IReadOnlyList<LearningExtraction>> EarlierOnTheStationAsync(SeriesMember judged, int most, CancellationToken cancellationToken)
        {
            AskedMost = most;

            return Task.FromResult<IReadOnlyList<LearningExtraction>>([.. Earlier.Take(most)]);
        }

        public Task<IReadOnlyList<LearningExtraction>> RecordsAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LearningExtraction>>([.. Held.Where(record => recordings.Contains(record.RecordingId))]);

        public Task<IReadOnlyList<LearningDataPart>> PartsAsync(RecordingId recording, IReadOnlyCollection<LearningDataKind> kinds, CancellationToken cancellationToken)
        {
            if (kinds.SequenceEqual([LearningDataKind.CornerOutlines]))
            {
                AskedOutlines.Add(recording);
            }

            return Task.FromResult<IReadOnlyList<LearningDataPart>>([]);
        }
    }
}
