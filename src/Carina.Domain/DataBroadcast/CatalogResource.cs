namespace Carina.Domain.DataBroadcast;

public sealed record CatalogResource
{
    public CatalogResource(string path, string mediaType)
    {
        Path = CarouselNumbers.Path(path, nameof(path));
        MediaType = CarouselNumbers.MediaType(mediaType, nameof(mediaType));
    }

    public string Path { get; }

    public string MediaType { get; }
}
