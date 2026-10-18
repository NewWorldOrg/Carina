using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// What matching recordings against the catalog reads of the records of their learning data, and of the
/// data itself.
/// </summary>
public interface ICatalogWorklist
{
    /// <summary>
    /// The record of the most recently started recording whose learning data
    /// <see cref="LearningExtraction.AwaitsMatching"/>; null when none does.
    /// </summary>
    Task<LearningExtraction?> NextAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The records read to their end or as far as they could be, other than <paramref name="heard"/>'s, of
    /// recordings started no more than <paramref name="within"/> before or after <paramref name="startedAt"/>.
    /// </summary>
    Task<IReadOnlyList<LearningExtraction>> AroundAsync(
        RecordingId heard,
        DateTime startedAt,
        TimeSpan within,
        CancellationToken cancellationToken);

    /// <summary>
    /// The copies of the programmes of the recordings named that have a record.
    /// </summary>
    Task<IReadOnlyDictionary<RecordingId, ProgrammeCopy>> ProgrammesAsync(
        IReadOnlyCollection<RecordingId> recordings,
        CancellationToken cancellationToken);

    /// <summary>
    /// The fingerprints and loudness kept for the recording; a chunk that cannot be read is heard as
    /// silence.
    /// </summary>
    Task<SoundTrack> SoundAsync(RecordingId recording, CancellationToken cancellationToken);
}
