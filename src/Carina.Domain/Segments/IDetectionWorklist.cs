using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// What detecting segments reads of the records of the learning data, and of the data itself.
/// </summary>
public interface IDetectionWorklist
{
    /// <summary>
    /// The record of the most recently started recording, still kept, whose detection by the version numbered
    /// <paramref name="modelVersion"/> <see cref="RecordingDetection.IsAwaited"/>, leaving out the recordings
    /// passed over; null when none waits.
    /// </summary>
    Task<LearningExtraction?> NextAsync(
        int modelVersion,
        IReadOnlyCollection<RecordingId> passedOver,
        CancellationToken cancellationToken);

    /// <summary>
    /// The records, read to their end or as far as they could be, of at most <paramref name="most"/> recordings
    /// on the station of <paramref name="judged"/> that started before it, the most recently started first.
    /// </summary>
    Task<IReadOnlyList<LearningExtraction>> EarlierOnTheStationAsync(
        SeriesMember judged,
        int most,
        CancellationToken cancellationToken);

    /// <summary>
    /// The records, read to their end or as far as they could be, of the recordings named.
    /// </summary>
    Task<IReadOnlyList<LearningExtraction>> RecordsAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken);

    /// <summary>
    /// The learning data of the kinds named kept for the recording; a chunk that cannot be read is left out.
    /// </summary>
    Task<IReadOnlyList<LearningDataPart>> PartsAsync(
        RecordingId recording,
        IReadOnlyCollection<LearningDataKind> kinds,
        CancellationToken cancellationToken);
}
