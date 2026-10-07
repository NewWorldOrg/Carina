using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// Thrown when the record of taking the learning data out of a recording was changed after the
/// writer read it.
/// </summary>
public sealed class LearningExtractionMovedMeanwhileException(RecordingId recordingId)
    : InvalidOperationException(
        $"The extraction of recording {recordingId?.Wire} changed after it was read, so what was written from that reading is dropped.")
{
    public RecordingId RecordingId { get; } = recordingId ?? throw new ArgumentNullException(nameof(recordingId));
}
