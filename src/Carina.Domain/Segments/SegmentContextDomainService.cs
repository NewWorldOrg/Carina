using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// What detecting one recording knows of it besides its own readings: the catalog's stretches heard in it, the corner
/// outlines of its station's earlier recordings to learn the logo from, and its earlier episodes.
/// </summary>
public sealed record SegmentSurroundings(
    IReadOnlyList<CatalogHeard> Heard,
    IReadOnlyList<RecordedCorners> Corners,
    IReadOnlyList<PastEpisode> Past);

/// <summary>
/// Decides what detecting one recording with one version of the model reads of what is known of it, looking up the
/// recordings read for their learning data, the catalog and the detections kept. The catalog's stretches heard in it
/// are each counted as the kind the stretch alone is taken for when that differs from its entry. The station's logo is
/// learned from the corner outlines of the most recent earlier recordings on its station that read something, as many
/// as asked, with the catalog's stretches heard in them. The earlier episodes are those of its series among the most
/// recent earlier recordings on the station, as many as asked, and the earlier recordings sharing the sound of a
/// programme fixture with it, wherever they were broadcast (<see cref="SeriesLinks"/>), each by the detection the same
/// version made of it; one that version threw away is no clue, and one it did not detect is left out.
/// </summary>
public sealed class SegmentContextDomainService(
    IDetectionWorklist worklist,
    ICatalogRepository catalog,
    IRecordingDetectionRepository detections)
{
    public async Task<SegmentSurroundings> GatherAsync(
        LearningExtraction record,
        int modelVersion,
        int episodesAmong,
        int logoFrom,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        RecordingId id = record.RecordingId;
        SeriesMember judged = record.Programme.MemberAs(id);
        IReadOnlyList<CatalogHeard> heard = [.. (await HeardInAsync([id], cancellationToken))[id]];
        IReadOnlyList<LearningExtraction> earlier = await worklist.EarlierOnTheStationAsync(judged, episodesAmong, cancellationToken);
        IReadOnlyList<RecordedCorners> corners = await CornersAsync(earlier.Take(logoFrom), cancellationToken);
        IReadOnlyList<PastEpisode> past = await PastAsync(judged, earlier, modelVersion, cancellationToken);

        return new SegmentSurroundings(heard, corners, past);
    }

    private static TimeSpan Starts(ProgrammeCopy programme) => programme.ProgrammeStartsAt - programme.RecordingStartedAt;

    private static TimeSpan? Ends(ProgrammeCopy programme) => programme.ProgrammeEndsAt - programme.RecordingStartedAt;

    private async Task<ILookup<RecordingId, CatalogHeard>> HeardInAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken)
    {
        IReadOnlyList<CatalogOccurrence> occurrences = recordings.Count is 0 ? [] : await catalog.OccurrencesInAsync(recordings, cancellationToken);
        IReadOnlyList<CatalogEntry> found = occurrences.Count is 0
            ? []
            : await catalog.EntriesAsync([.. occurrences.Select(occurrence => occurrence.EntryId).Distinct()], cancellationToken);
        Dictionary<CatalogEntryId, CatalogEntry> entries = found.ToDictionary(entry => entry.Id);

        return occurrences
            .Where(occurrence => entries.ContainsKey(occurrence.EntryId))
            .ToLookup(occurrence => occurrence.RecordingId, occurrence => CatalogHeard.Of(occurrence, entries[occurrence.EntryId]));
    }

    private async Task<IReadOnlyList<RecordedCorners>> CornersAsync(IEnumerable<LearningExtraction> records, CancellationToken cancellationToken)
    {
        LearningExtraction[] read = [.. records.Where(record => record.ReadThrough > TimeSpan.Zero)];
        ILookup<RecordingId, CatalogHeard> heard = await HeardInAsync([.. read.Select(record => record.RecordingId)], cancellationToken);
        List<RecordedCorners> corners = [];

        foreach (LearningExtraction record in read)
        {
            IReadOnlyList<LearningDataPart> parts = await worklist.PartsAsync(record.RecordingId, [LearningDataKind.CornerOutlines], cancellationToken);

            if (parts.Count is 0)
            {
                continue;
            }

            byte[] outlines = SegmentEvidence.Of(parts, record.ReadThrough).Outlines.ToArray();

            corners.Add(new RecordedCorners(record.Programme.MemberAs(record.RecordingId), outlines, [.. heard[record.RecordingId]]));
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

        return
        [
            .. members
                .Where(detected.ContainsKey)
                .Select(member => new PastEpisode(member, Starts(copies[member]), Ends(copies[member]), detected[member].Segments)),
        ];
    }
}
