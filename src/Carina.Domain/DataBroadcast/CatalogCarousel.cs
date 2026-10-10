namespace Carina.Domain.DataBroadcast;

/// <summary>
/// One carousel of the catalog: its component tag, its download id and the modules it holds valid, in the
/// order its download info lists them.
/// </summary>
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
