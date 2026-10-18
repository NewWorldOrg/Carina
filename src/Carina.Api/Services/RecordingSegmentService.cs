using Carina.Api.Common;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

public enum RecordingSegmentFailure
{
    NoSuchRecording = 1,
}

/// <summary>
/// The stretches of one recording as its detail shows them, in the recording's own time: how long the recording
/// was written for, and how its stretches stand by the version of the model in use.
/// </summary>
public sealed record RecordingSegments(TimeSpan Length, SegmentStanding Standing);

/// <summary>
/// Reads where the stretches of a recording stand. A recording still being written is answered as any other;
/// one whose row is gone is not, whatever is kept of it. Nothing here writes.
/// </summary>
public sealed class RecordingSegmentService(
    IRecordingDirectory recordings,
    ISegmentModelVersionRepository versions,
    IRecordingDetectionRepository detections)
{
    public async Task<ServiceResult<RecordingSegments, RecordingSegmentFailure>> ReadAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return ServiceResult<RecordingSegments, RecordingSegmentFailure>.Failure(
                $"There is no recording {id.Wire}.",
                RecordingSegmentFailure.NoSuchRecording);
        }

        SegmentModelVersion version = await versions.ActiveAsync(cancellationToken);
        RecordingDetection? detection = await detections.FindAsync(id, version.Number, cancellationToken);

        return ServiceResult<RecordingSegments, RecordingSegmentFailure>.Success(new RecordingSegments(recording.Written, SegmentStanding.Of(null, detection)));
    }
}
