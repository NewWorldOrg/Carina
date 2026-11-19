using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public enum LearningExtractionWrite
{
    Written = 1,

    AnotherIsReading = 2,
}

public interface ILearningExtractionRepository
{
    Task<LearningExtraction?> FindAsync(RecordingId recordingId, CancellationToken cancellationToken);

    Task<IReadOnlyList<LearningExtraction>> ListAsync(
        LearningExtractionState state,
        int limit,
        CancellationToken cancellationToken);

    Task AddAsync(LearningExtraction extraction, CancellationToken cancellationToken);

    Task<LearningExtractionWrite> SaveAsync(LearningExtraction extraction, CancellationToken cancellationToken);
}
