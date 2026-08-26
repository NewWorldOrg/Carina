using Carina.Api.Services;
using Carina.Infrastructure.Thumbnails;

using RecordedProgramme = Carina.Domain.Recordings.Recording;

namespace Carina.Api.Responder.Recordings;

public sealed record RecordingReconciliationResponder(
    bool SizeObserved,
    long? FileSizeBytes,
    DateTime? ObservedAt,
    long WrittenDurationMs,
    RecordingWindowResponder ExpectedWindow,
    double Coverage,
    bool StoppedUnasked);

public sealed record DropBucketResponder(int Second, long Continuity, long Scrambled);

public sealed record PcrReanchorResponder(int Second, long Before, long After);

public sealed record RecordingPositionsResponder(
    bool Located,
    long? AnchorPcr,
    IReadOnlyList<DropBucketResponder> Buckets,
    IReadOnlyList<PcrReanchorResponder> Reanchors);

public sealed record RecordingDetailResponder(
    RecordingResponder Recording,
    RecordingReconciliationResponder Reconciliation,
    IReadOnlyList<RecordingInterruptionResponder> Interruptions,
    RecordingPositionsResponder Positions)
{
    public static RecordingDetailResponder Of(RecordedProgramme recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        RecordingWindowResponder window = RecordingResponder.Window(recording);

        return new RecordingDetailResponder(
            RecordingResponder.Of(recording),
            new RecordingReconciliationResponder(
                recording.FileSizeObserved is not null,
                recording.FileSizeObserved,
                recording.ObservedAt,
                recording.WrittenDurationMs,
                window,
                (double)recording.WrittenDurationMs / window.DurationMs,
                recording.AbortedAt is null),
            [
                .. recording.Interruptions.Select(interruption => new RecordingInterruptionResponder(
                    interruption.Fault,
                    interruption.OccurredAt,
                    interruption.ResumedAt)),
            ],
            new RecordingPositionsResponder(
                recording.Positions.Located,
                recording.Positions.AnchorPcr,
                [
                    .. recording.Positions.Buckets.Select(bucket => new DropBucketResponder(
                        bucket.Second,
                        bucket.Continuity,
                        bucket.Scrambled)),
                ],
                [
                    .. recording.Positions.Reanchors.Select(reanchor => new PcrReanchorResponder(
                        reanchor.Second,
                        reanchor.Before,
                        reanchor.After)),
                ]));
    }
}

public sealed record ThumbnailRemakeResponder(
    ThumbnailRemake Remake,
    RecordingThumbnailResponder Thumbnail)
{
    public static ThumbnailRemakeResponder Of(ThumbnailRemade remade)
    {
        ArgumentNullException.ThrowIfNull(remade);

        return new ThumbnailRemakeResponder(
            remade.Remake,
            new RecordingThumbnailResponder(
                remade.Recording.ThumbnailState,
                remade.Recording.ThumbnailFault,
                remade.Recording.ThumbnailShowsAnUnfinishedRecording));
    }
}
