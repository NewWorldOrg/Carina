namespace Carina.Domain.DataBroadcast;

/// <summary>
/// One module the catalog holds valid: its id, version and size, whether that version has arrived, and the
/// resources it carries once it has.
/// </summary>
public sealed record CatalogModule
{
    public CatalogModule(int id, int version, long size, bool arrived, IReadOnlyList<CatalogResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        if (!arrived && resources.Count > 0)
        {
            throw new ArgumentException("A module that has not arrived carries no resources yet.", nameof(resources));
        }

        Id = CarouselNumbers.ModuleId(id, nameof(id));
        Version = CarouselNumbers.Version(version, nameof(version));
        Size = size;
        Arrived = arrived;
        Resources = resources;
    }

    public int Id { get; }

    public int Version { get; }

    public long Size { get; }

    public bool Arrived { get; }

    public IReadOnlyList<CatalogResource> Resources { get; }
}
