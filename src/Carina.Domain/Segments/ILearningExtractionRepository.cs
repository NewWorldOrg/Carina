using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public enum LearningExtractionWrite
{
    Written = 1,

    AnotherIsReading = 2,
}

/// <summary>
/// Where the record of taking the learning data out of each recording is kept, one row per
/// recording.
/// </summary>
public interface ILearningExtractionRepository
{
    Task<LearningExtraction?> FindAsync(RecordingId recordingId, CancellationToken cancellationToken);

    /// <summary>
    /// The extractions in <paramref name="state"/>, the most recently recorded first.
    /// </summary>
    Task<IReadOnlyList<LearningExtraction>> ListAsync(
        LearningExtractionState state,
        int limit,
        CancellationToken cancellationToken);

    Task AddAsync(LearningExtraction extraction, CancellationToken cancellationToken);

    Task<LearningExtractionWrite> SaveAsync(LearningExtraction extraction, CancellationToken cancellationToken);
}
