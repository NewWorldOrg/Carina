using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

public sealed record DetectionInputs(SegmentEvidence Evidence, SegmentContext Context, DateTime CatalogUntil);

public sealed class SegmentDetecting(
    IDetectionWorklist worklist,
    SegmentContextDomainService context,
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
        SegmentSurroundings known = await context.GatherAsync(record, modelVersion, settings.EpisodesAmong, settings.LogoFrom, cancellationToken);
        CornerLogo logo = await lowest.Run(() => CornerLogo.Learn(judged, known.Corners), cancellationToken);
        SegmentEvidence evidence = await lowest.Run(() => SegmentEvidence.Of(parts, record.ReadThrough), cancellationToken);

        return new DetectionInputs(evidence, SegmentContext.Of(record.Programme, known.Heard, logo, known.Past), at);
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
}
