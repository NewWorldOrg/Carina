using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public interface ILearningDataRepository
{
    Task KeepAsync(LearningDataBlock block, CancellationToken cancellationToken);

    Task<LearningDataBlock?> FindAsync(
        RecordingId recordingId,
        LearningDataKind kind,
        int chunk,
        CancellationToken cancellationToken);

    Task<int> CountAsync(RecordingId recordingId, CancellationToken cancellationToken);

    Task<long> SizeAsync(CancellationToken cancellationToken);
}
