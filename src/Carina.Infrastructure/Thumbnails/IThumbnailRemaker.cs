using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Thumbnails;

public interface IThumbnailRemaker
{
    Task<ThumbnailRemake> RemakeAsync(RecordingId id, CancellationToken cancellationToken);
}
