using Carina.Api.Common;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

public enum RecordingSegmentFailure
{
    NoSuchRecording = 1,
}

public sealed record RecordingSegments(TimeSpan Length, SegmentStanding Standing);

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

        return ServiceResult<RecordingSegments, RecordingSegmentFailure>.Success(new RecordingSegments(recording.Written, SegmentStanding.Of(detection)));
    }
}
