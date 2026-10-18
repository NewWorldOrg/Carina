using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// Where the detections are kept, one per recording and version of the model. Nothing is removed from it,
/// and a recording thrown away leaves its detections where they are.
/// </summary>
public interface IRecordingDetectionRepository
{
    Task<RecordingDetection?> FindAsync(RecordingId recording, int modelVersion, CancellationToken cancellationToken);

    /// <summary>
    /// The detections of the recordings named made by the version numbered <paramref name="modelVersion"/>.
    /// </summary>
    Task<IReadOnlyList<RecordingDetection>> ListAsync(
        IReadOnlyCollection<RecordingId> recordings,
        int modelVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes the detection, in place of the one already kept for the same recording and version.
    /// </summary>
    Task KeepAsync(RecordingDetection detection, CancellationToken cancellationToken);
}
