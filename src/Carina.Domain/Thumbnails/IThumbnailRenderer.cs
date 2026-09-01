namespace Carina.Domain.Thumbnails;

public interface IThumbnailRenderer
{
    Task<ThumbnailRender> RenderAsync(ThumbnailRequest request, CancellationToken cancellationToken);

    Task<ThumbnailRender> FrameAsync(ThumbnailFrameRequest request, CancellationToken cancellationToken);
}
