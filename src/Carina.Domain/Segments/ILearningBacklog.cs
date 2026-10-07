using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// A recording that has ended, and the record of taking its learning data out when one is kept.
/// </summary>
public sealed record BackloggedRecording(Recording Recording, LearningExtraction? Record);

/// <summary>
/// What reading the learning data out of the recordings that have ended reads of them.
/// </summary>
public interface ILearningBacklog
{
    /// <summary>
    /// The recordings that have ended under the output roots named and whose learning data waits to be
    /// read from their files with <see cref="ExtractionVersion.Current"/>: those with no record, and those
    /// whose record <see cref="LearningExtraction.AwaitsReading"/>. The most recently started first.
    /// </summary>
    Task<IReadOnlyList<BackloggedRecording>> AwaitingAsync(
        IReadOnlyList<OutputRoot> withinReach,
        int atMost,
        CancellationToken cancellationToken);

    /// <summary>
    /// The records done or left partway, with something read, of recordings whose captions are ready,
    /// that keep no first chunk of whether captions are shown written since both the captions and the
    /// record last changed. The most recently started first.
    /// </summary>
    Task<IReadOnlyList<LearningExtraction>> UncaptionedAsync(int atMost, CancellationToken cancellationToken);
}
