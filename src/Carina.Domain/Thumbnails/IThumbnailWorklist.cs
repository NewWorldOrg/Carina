using Carina.Domain.Recordings;

namespace Carina.Domain.Thumbnails;

public interface IThumbnailWorklist
{
    Task<IReadOnlyList<ThumbnailSubject>> AwaitingAsync(int atMost, CancellationToken cancellationToken);

    Task IllustrateAsync(
        RecordingId id,
        ThumbnailState state,
        ThumbnailFault? fault,
        CancellationToken cancellationToken);

    Task<ThumbnailSubject?> AskAgainAsync(RecordingId id, CancellationToken cancellationToken);
}
