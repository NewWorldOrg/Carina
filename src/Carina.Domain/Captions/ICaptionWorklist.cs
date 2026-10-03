using Carina.Domain.Recordings;

namespace Carina.Domain.Captions;

public interface ICaptionWorklist
{
    /// <summary>
    /// The ended recordings under a root within reach whose captions are to be taken, newest first.
    /// </summary>
    Task<IReadOnlyList<CaptionSubject>> AwaitingAsync(
        IReadOnlyList<OutputRoot> withinReach,
        int atMost,
        CancellationToken cancellationToken);

    Task<int> WaitingOutOfReachAsync(IReadOnlyList<OutputRoot> withinReach, CancellationToken cancellationToken);

    Task<bool> AnyBeingRecordedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The recordings whose row says their captions are ready.
    /// </summary>
    Task<IReadOnlyList<RecordingId>> ReadyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Keeps where the captions of a recording stand, and answers false when the recording is no longer
    /// in the ledger.
    /// </summary>
    Task<bool> CaptionAsync(RecordingId id, CaptionState state, int? pictures, CancellationToken cancellationToken);
}
