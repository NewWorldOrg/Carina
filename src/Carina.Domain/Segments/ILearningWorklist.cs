using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// What following recordings for their learning data reads of the recordings.
/// </summary>
public interface ILearningWorklist
{
    /// <summary>
    /// The recordings still being written under the output roots named, the earliest started first.
    /// </summary>
    Task<IReadOnlyList<Recording>> BeingRecordedAsync(IReadOnlyList<OutputRoot> withinReach, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the recording is still being written or has ended; null when no such recording is kept.
    /// </summary>
    Task<RecordingStanding?> StandingAsync(RecordingId id, CancellationToken cancellationToken);
}
