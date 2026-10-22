using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Reads where an encode of a recording takes its chapters from: the version of the model in use and how the
/// recording stands by that version's detection, read the way playback reads them.
/// </summary>
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
