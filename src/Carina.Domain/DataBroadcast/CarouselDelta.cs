namespace Carina.Domain.DataBroadcast;

/// <summary>
/// What changed in a <see cref="CarouselState"/>: the catalog, a module version that arrived, an event
/// message that came, the data broadcast found not to be there, or a carousel left out.
/// </summary>
public abstract record CarouselDelta
{
    private CarouselDelta()
    {
    }

    public sealed record CatalogChanged(CarouselCatalog Catalog) : CarouselDelta;

    public sealed record ModuleArrived(uint DownloadId, ModuleVersion Module) : CarouselDelta;

    public sealed record EventCame(EventMessage Message) : CarouselDelta;

    public sealed record Absent : CarouselDelta;

    public sealed record CarouselDropped(int Tag, CarouselDropReason Reason) : CarouselDelta;
}
