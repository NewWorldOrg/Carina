using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Reads where an encode of a recording takes its chapters from: whether learning is on, the version of the
/// model in use and how the recording stands by that version's detection, read the way playback reads them.
/// </summary>
public sealed class EncodeChapterChoices(
    ILearningSwitch learning,
    ISegmentModelVersionRepository versions,
    IRecordingDetectionRepository detections)
{
    public async Task<EncodeChapterChoice> ReadAsync(RecordingId recording, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        bool on = await learning.IsOnAsync(cancellationToken);
        SegmentModelVersion version = await versions.ActiveAsync(cancellationToken);
        RecordingDetection? detection = await detections.FindAsync(recording, version.Number, cancellationToken);

        return EncodeChapterChoice.Of(on, version.Number, SegmentStanding.Of(null, detection));
    }
}
