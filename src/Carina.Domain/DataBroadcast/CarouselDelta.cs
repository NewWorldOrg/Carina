namespace Carina.Domain.DataBroadcast;

public abstract record CarouselDelta
{
    private CarouselDelta()
    {
    }

    public sealed record CatalogChanged(CarouselCatalog Catalog) : CarouselDelta;

    public sealed record ModuleArrived(uint DownloadId, ModuleVersion Module) : CarouselDelta;

    public sealed record EventCame(EventMessage Message) : CarouselDelta;

    public sealed record NptReferred(NptReference Reference) : CarouselDelta;

    public sealed record Absent : CarouselDelta;

    public sealed record CarouselDropped(int Tag, CarouselDropReason Reason) : CarouselDelta;
}
