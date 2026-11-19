using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

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
