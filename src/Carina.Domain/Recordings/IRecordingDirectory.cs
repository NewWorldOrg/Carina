using Carina.Domain.Base;

namespace Carina.Domain.Recordings;

public enum RecordingHalt
{
    Written = 1,

    NoSuchRecording = 2,

    AlreadyEnded = 3,
}

public interface IRecordingDirectory
{
    Task<PaginatedList<Recording>> ListAsync(RecordingQuery query, CancellationToken cancellationToken);

    Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken);

    Task<RecordingHalt> HaltAsync(
        RecordingId id,
        RecordingStopReason reason,
        DateTime at,
        CancellationToken cancellationToken);
}
