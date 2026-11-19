using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public interface IDetectionWorklist
{
    Task<LearningExtraction?> NextAsync(
        int modelVersion,
        IReadOnlyCollection<RecordingId> passedOver,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LearningExtraction>> EarlierOnTheStationAsync(
        SeriesMember judged,
        int most,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LearningExtraction>> RecordsAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken);

    Task<IReadOnlyList<LearningDataPart>> PartsAsync(
        RecordingId recording,
        IReadOnlyCollection<LearningDataKind> kinds,
        CancellationToken cancellationToken);
}
