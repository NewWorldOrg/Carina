using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Encodings;

public sealed class EncodeChapterChoices(
    ISegmentModelVersionRepository versions,
    IRecordingDetectionRepository detections)
{
    public async Task<EncodeChapterChoice> ReadAsync(RecordingId recording, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        SegmentModelVersion version = await versions.ActiveAsync(cancellationToken);
        RecordingDetection? detection = await detections.FindAsync(recording, version.Number, cancellationToken);

        return EncodeChapterChoice.Of(version.Number, SegmentStanding.Of(detection));
    }
}
