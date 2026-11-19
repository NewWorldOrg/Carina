using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public interface IRecordingDetectionRepository
{
    Task<RecordingDetection?> FindAsync(RecordingId recording, int modelVersion, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecordingDetection>> ListAsync(
        IReadOnlyCollection<RecordingId> recordings,
        int modelVersion,
        CancellationToken cancellationToken);

    Task KeepAsync(RecordingDetection detection, CancellationToken cancellationToken);
}
