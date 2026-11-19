using Carina.Domain.Recordings;

namespace Carina.Domain.Captions;

public interface ICaptionWorklist
{
    Task<IReadOnlyList<CaptionSubject>> AwaitingAsync(
        IReadOnlyList<OutputRoot> withinReach,
        int atMost,
        CancellationToken cancellationToken);

    Task<int> WaitingOutOfReachAsync(IReadOnlyList<OutputRoot> withinReach, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecordingId>> ReadyAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CaptionSubject>> ReadyAmongAsync(
        IReadOnlyCollection<RecordingId> among,
        IReadOnlyList<OutputRoot> withinReach,
        int atMost,
        CancellationToken cancellationToken);

    Task<bool> CaptionAsync(RecordingId id, CaptionState state, int? pictures, CancellationToken cancellationToken);
}
