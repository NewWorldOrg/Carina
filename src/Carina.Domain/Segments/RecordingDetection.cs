using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// The detection of one recording by one version of the model, kept for good under the recording's id by
/// value: how it ended, its stretches end to end from the recording's zero, kept even when the sequence was
/// thrown away, the share of the length breaks take, the boundary candidates' seconds, the version of the
/// extraction the readings were taken with, the moment up to which the catalog it read was matched, and when
/// it was made. Detecting the recording again with the same version takes its place.
/// </summary>
public sealed class RecordingDetection
{
    private RecordingDetection()
    {
    }

    public RecordingId RecordingId { get; private set; } = null!;

    public int ModelVersion { get; private set; }

    public SegmentDetectionState State { get; private set; }

    public IReadOnlyList<SegmentSpan> Segments { get; private set; } = [];

    public double CmShare { get; private set; }

    public IReadOnlyList<TimeSpan> Snaps { get; private set; } = [];

    public int ExtractionVersionNumber { get; private set; }

    public DateTime CatalogUntil { get; private set; }

    public DateTime DetectedAt { get; private set; }

    public static RecordingDetection Of(
        RecordingId recordingId,
        int modelVersion,
        SegmentDetection detection,
        int extractionVersionNumber,
        DateTime catalogUntil,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(detection);

        return Rehydrate(
            recordingId,
            modelVersion,
            detection.State,
            detection.Segments,
            detection.CmShare,
            detection.Snaps,
            extractionVersionNumber,
            catalogUntil,
            at);
    }

    public static RecordingDetection Rehydrate(
        RecordingId recordingId,
        int modelVersion,
        SegmentDetectionState state,
        IReadOnlyList<SegmentSpan> segments,
        double cmShare,
        IReadOnlyList<TimeSpan> snaps,
        int extractionVersionNumber,
        DateTime catalogUntil,
        DateTime detectedAt)
    {
        ArgumentNullException.ThrowIfNull(recordingId);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(snaps);
        ArgumentOutOfRangeException.ThrowIfLessThan(modelVersion, SegmentModelVersion.FirstNumber);
        ArgumentOutOfRangeException.ThrowIfLessThan(extractionVersionNumber, 1);

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "A detection ends in one of the three states kept.");
        }

        if (!(cmShare is >= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(cmShare), cmShare, "The share breaks take lies from 0 to 1.");
        }

        string? fault = Untiled(segments) ?? Unsorted(snaps);

        if (fault is null && state is SegmentDetectionState.Marked && segments.Count < 2)
        {
            fault = "A detection that marked breaks holds at least two stretches.";
        }

        return fault is null
            ? new RecordingDetection
            {
                RecordingId = recordingId,
                ModelVersion = modelVersion,
                State = state,
                Segments = [.. segments],
                CmShare = cmShare,
                Snaps = [.. snaps],
                ExtractionVersionNumber = extractionVersionNumber,
                CatalogUntil = UtcTimes.Required(catalogUntil, nameof(catalogUntil)),
                DetectedAt = UtcTimes.Required(detectedAt, nameof(detectedAt)),
            }
            : throw new ArgumentException(fault);
    }

    /// <summary>
    /// Whether the recording of <paramref name="record"/> waits to be detected, given what <paramref name="kept"/>
    /// holds of its detection by the version in use: its data was read to its end or as far as it could be,
    /// reached past zero and has been matched against the catalog since it last changed, and no detection by that
    /// version was made since it was matched.
    /// </summary>
    public static bool IsAwaited(LearningExtraction record, RecordingDetection? kept)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (kept is not null && !kept.RecordingId.Equals(record.RecordingId))
        {
            throw new ArgumentException("A record is judged beside the detection of its own recording.", nameof(kept));
        }

        return record.State is LearningExtractionState.Done or LearningExtractionState.Partial
               && record.ReadThrough > TimeSpan.Zero
               && !record.AwaitsMatching
               && (kept is null || kept.DetectedAt < record.MatchedAt);
    }

    private static string? Untiled(IReadOnlyList<SegmentSpan> segments)
    {
        if (segments.Count is 0 || segments.Any(segment => segment is null) || segments[0].Starts != TimeSpan.Zero)
        {
            return "A detection covers its recording from zero.";
        }

        return segments.Zip(segments.Skip(1)).Any(pair => pair.Second.Starts != pair.First.Ends)
            ? "A detection lays its stretches end to end."
            : null;
    }

    private static string? Unsorted(IReadOnlyList<TimeSpan> snaps)
        => snaps.Zip(snaps.Skip(1)).Any(pair => pair.Second < pair.First) || snaps.Any(snap => snap < TimeSpan.Zero)
            ? "The boundary candidates' seconds lie in order from zero."
            : null;
}
