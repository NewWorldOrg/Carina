using Carina.Domain.Recordings;

namespace Carina.Domain.Thumbnails;

public interface IThumbnailWorklist
{
    Task<IReadOnlyList<ThumbnailSubject>> AwaitingAsync(
        IReadOnlyList<OutputRoot> withinReach,
        int atMost,
        CancellationToken cancellationToken);

    Task<int> WaitingOutOfReachAsync(IReadOnlyList<OutputRoot> withinReach, CancellationToken cancellationToken);

    Task IllustrateAsync(
        RecordingId id,
        ThumbnailState state,
        ThumbnailFault? fault,
        CancellationToken cancellationToken);

    Task<ThumbnailSubject?> AskAgainAsync(RecordingId id, CancellationToken cancellationToken);
}
