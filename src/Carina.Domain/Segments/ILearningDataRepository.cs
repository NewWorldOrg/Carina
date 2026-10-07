using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// Where the learning data is kept, one row per recording, kind and chunk.
/// </summary>
public interface ILearningDataRepository
{
    /// <summary>
    /// Writes the block, in place of the one already kept for the same recording, kind and chunk.
    /// </summary>
    Task KeepAsync(LearningDataBlock block, CancellationToken cancellationToken);

    Task<LearningDataBlock?> FindAsync(
        RecordingId recordingId,
        LearningDataKind kind,
        int chunk,
        CancellationToken cancellationToken);

    Task<int> CountAsync(RecordingId recordingId, CancellationToken cancellationToken);

    /// <summary>
    /// The bytes of all the learning data kept.
    /// </summary>
    Task<long> SizeAsync(CancellationToken cancellationToken);
}
