namespace Carina.Domain.DataBroadcast;

/// <summary>
/// One resource as the catalog names it: its path and media type.
/// </summary>
public sealed record CatalogResource
{
    public CatalogResource(string path, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(mediaType);

        Path = path;
        MediaType = mediaType;
    }

    public string Path { get; }

    public string MediaType { get; }
}
