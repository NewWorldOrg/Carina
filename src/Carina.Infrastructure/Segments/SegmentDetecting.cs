using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// What a detection of one recording reads: its readings, what is known of it besides, and the moment up to
/// which the catalog it heard was matched.
/// </summary>
public sealed record DetectionInputs(SegmentEvidence Evidence, SegmentContext Context, DateTime CatalogUntil);

/// <summary>
/// Detects the segments of one recording with one version of the model. It reads the recording's readings;
/// the catalog's stretches heard in it, each counted as the kind the stretch alone is taken for when that
/// differs from its entry; the corner outlines of the <see cref="SegmentDetectionSettings.LogoFrom"/> most
/// recent earlier recordings on its station, to learn the station's logo from; and the earlier episodes of its
/// series, found among the <see cref="SegmentDetectionSettings.EpisodesAmong"/> most recent earlier recordings on
/// the station and the recordings sharing the sound of a programme fixture with it, each by the detection the same
/// version made of it, leaving out one thrown away, and by the confirmed answer given for it while that stands, its
/// openings and endings still read from the detection while the answer could not name them, with where its programme
/// starts and ends. The counting is done on the <see cref="LowestPriorityScheduler"/>. Nothing
/// is written here.
/// </summary>
public sealed class SegmentDetecting(
    IDetectionWorklist worklist,
    ICatalogRepository catalog,
    IRecordingDetectionRepository detections,
    ISegmentAnswerRepository answers,
    LowestPriorityScheduler lowest,
    SegmentDetectionSettings settings)
{
    public static readonly IReadOnlyList<LearningDataKind> Readings =
    [
        LearningDataKind.Loudness,
        LearningDataKind.ChannelDifferences,
        LearningDataKind.FrameLights,
        LearningDataKind.CornerOutlines,
        LearningDataKind.CaptionPresence,
    ];

    public async Task<DetectionInputs> GatherAsync(LearningExtraction record, int modelVersion, DateTime at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        RecordingId id = record.RecordingId;
        SeriesMember judged = record.Programme.MemberAs(id);
        IReadOnlyList<LearningDataPart> parts = await worklist.PartsAsync(id, Readings, cancellationToken);
        IReadOnlyList<CatalogHeard> heard = await HeardAsync(id, cancellationToken);
        IReadOnlyList<LearningExtraction> earlier = await worklist.EarlierOnTheStationAsync(judged, settings.EpisodesAmong, cancellationToken);
        IReadOnlyList<RecordedCorners> corners = await CornersAsync(earlier.Take(settings.LogoFrom), cancellationToken);
        IReadOnlyList<PastEpisode> past = await PastAsync(judged, earlier, modelVersion, cancellationToken);
        CornerLogo logo = await lowest.Run(() => CornerLogo.Learn(judged, corners), cancellationToken);
        SegmentEvidence evidence = await lowest.Run(() => SegmentEvidence.Of(parts, record.ReadThrough), cancellationToken);

        return new DetectionInputs(evidence, SegmentContext.Of(record.Programme, heard, logo, past), at);
    }

    public async Task<RecordingDetection> DetectAsync(
        LearningExtraction record,
        SegmentModelVersion version,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(version);

        ExtractionVersion extracted = record.Version
            ?? throw new ArgumentException("A recording is detected from data taken out with a version of the extraction.", nameof(record));
        DetectionInputs inputs = await GatherAsync(record, version.Number, at, cancellationToken);
        SegmentModel model = version.Model;
        SegmentDetection detection = await lowest.Run(() => SegmentDetector.Detect(inputs.Evidence, inputs.Context, model), cancellationToken);

        return RecordingDetection.Of(record.RecordingId, version.Number, detection, extracted.Number, inputs.CatalogUntil, at);
    }

    private async Task<IReadOnlyList<CatalogHeard>> HeardAsync(RecordingId id, CancellationToken cancellationToken)
    {
        IReadOnlyList<CatalogOccurrence> occurrences = await catalog.OccurrencesInAsync([id], cancellationToken);

        if (occurrences.Count is 0)
        {
            return [];
        }

        Dictionary<CatalogEntryId, CatalogEntry> entries = (await catalog.EntriesAsync([.. occurrences.Select(occurrence => occurrence.EntryId).Distinct()], cancellationToken))
            .ToDictionary(entry => entry.Id);

        return [.. occurrences.Where(occurrence => entries.ContainsKey(occurrence.EntryId)).Select(occurrence => CatalogHeard.Of(occurrence, entries[occurrence.EntryId]))];
    }

    private async Task<IReadOnlyList<RecordedCorners>> CornersAsync(IEnumerable<LearningExtraction> records, CancellationToken cancellationToken)
    {
        List<RecordedCorners> corners = [];

        foreach (LearningExtraction record in records.Where(record => record.ReadThrough > TimeSpan.Zero))
        {
            IReadOnlyList<LearningDataPart> parts = await worklist.PartsAsync(record.RecordingId, [LearningDataKind.CornerOutlines], cancellationToken);

            if (parts.Count is 0)
            {
                continue;
            }

            byte[] outlines = SegmentEvidence.Of(parts, record.ReadThrough).Outlines.ToArray();

            corners.Add(new RecordedCorners(record.Programme.MemberAs(record.RecordingId), outlines));
        }

        return corners;
    }

    private async Task<IReadOnlyList<PastEpisode>> PastAsync(
        SeriesMember judged,
        IReadOnlyList<LearningExtraction> earlier,
        int modelVersion,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IReadOnlyList<RecordingId>> sharers = await catalog.SoundSharersAsync(cancellationToken);
        RecordingId[] sharing = [.. sharers.Where(group => group.Contains(judged.RecordingId)).SelectMany(group => group).Distinct()];
        IReadOnlyList<LearningExtraction> elsewhere = sharing.Length is 0 ? [] : await worklist.RecordsAsync(sharing, cancellationToken);
        Dictionary<RecordingId, ProgrammeCopy> copies = earlier
            .Concat(elsewhere.Where(record => record.Programme.RecordingStartedAt < judged.StartedAt))
            .Where(record => !record.RecordingId.Equals(judged.RecordingId))
            .DistinctBy(record => record.RecordingId)
            .ToDictionary(record => record.RecordingId, record => record.Programme);
        IReadOnlyList<SeriesMember> series = SeriesLinks.SharingSound(sharers).SeriesOf(judged, [.. copies.Select(pair => pair.Value.MemberAs(pair.Key))]);

        if (series.Count is 0)
        {
            return [];
        }

        RecordingId[] members = [.. series.Select(member => member.RecordingId)];
        Dictionary<RecordingId, RecordingDetection> detected = (await detections.ListAsync(members, modelVersion, cancellationToken))
            .Where(detection => detection.State is not SegmentDetectionState.Discarded)
            .ToDictionary(detection => detection.RecordingId);
        Dictionary<RecordingId, RecordingAnswer> answered = (await answers.ListAsync(members, cancellationToken))
            .Where(answer => answer is { Stands: true, Confirmed: true })
            .ToDictionary(answer => answer.RecordingId);

        return
        [
            .. members
                .Where(member => detected.ContainsKey(member) || answered.ContainsKey(member))
                .Select(member => new PastEpisode(
                    member,
                    Starts(copies[member]),
                    Ends(copies[member]),
                    detected.GetValueOrDefault(member)?.Segments,
                    answered.GetValueOrDefault(member)?.Segments,
                    answered.GetValueOrDefault(member)?.NamesOpeningsAndEndings ?? false)),
        ];
    }

    private static TimeSpan Starts(ProgrammeCopy programme) => programme.ProgrammeStartsAt - programme.RecordingStartedAt;

    private static TimeSpan? Ends(ProgrammeCopy programme) => programme.ProgrammeEndsAt - programme.RecordingStartedAt;
}
