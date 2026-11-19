namespace Carina.Domain.DataBroadcast;

public sealed record CatalogCarousel
{
    public CatalogCarousel(int tag, uint downloadId, IReadOnlyList<CatalogModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        if (modules.Select(module => module.Id).Distinct().Count() != modules.Count)
        {
            throw new ArgumentException("A carousel holds each module once.", nameof(modules));
        }

        Tag = CarouselNumbers.Tag(tag, nameof(tag));
        DownloadId = downloadId;
        Modules = [.. modules];
    }

    public int Tag { get; }

    public uint DownloadId { get; }

    public IReadOnlyList<CatalogModule> Modules { get; }
}
