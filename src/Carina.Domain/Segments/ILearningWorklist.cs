using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// What following recordings for their learning data reads of the recordings and their programmes.
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

    /// <summary>
    /// When the recording's programme ends by the guide, or else by its reservation once that end has
    /// been announced; null when neither says, or what they say is no later than the programme starts.
    /// </summary>
    Task<DateTime?> ProgrammeEndsAtAsync(Recording recording, CancellationToken cancellationToken);
}
