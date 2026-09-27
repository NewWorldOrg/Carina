using Carina.Domain.Base;
using Carina.Domain.Quality;

namespace Carina.Domain.Recordings;

public enum RecordingHalt
{
    Written = 1,

    NoSuchRecording = 2,

    AlreadyEnded = 3,
}

public enum RecordingDiscard
{
    Discarded = 1,

    NoSuchRecording = 2,

    StillRecording = 3,
}

public enum RecordingErasureNote
{
    Noted = 1,

    NoSuchRecording = 2,

    StillRecording = 3,
}

public interface IRecordingDirectory
{
    Task<RecordingErasureNote> NoteErasureAsync(
        RecordingId id,
        RecordingErasure erasure,
        DateTime at,
        CancellationToken cancellationToken);

    Task<PaginatedList<Recording>> ListAsync(
        RecordingQuery query,
        QualityBands bands,
        CancellationToken cancellationToken);

    Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken);

    Task<RecordingHalt> HaltAsync(
        RecordingId id,
        RecordingStopReason reason,
        DateTime at,
        CancellationToken cancellationToken);

    Task<RecordingDiscard> DiscardAsync(RecordingId id, CancellationToken cancellationToken);
}
