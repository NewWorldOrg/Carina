using Carina.Broadcast.Tables;

namespace Carina.Broadcast.DsmCc;

public abstract record CarouselChange
{
    private CarouselChange(int componentTag)
    {
        ComponentTag = componentTag;
    }

    public int ComponentTag { get; }

    public sealed record CatalogueUpdated : CarouselChange
    {
        internal CatalogueUpdated(int componentTag, DataCarouselCatalogue catalogue)
            : base(componentTag)
        {
            Catalogue = catalogue;
        }

        public DataCarouselCatalogue Catalogue { get; }
    }

    public sealed record ModuleCompleted : CarouselChange
    {
        internal ModuleCompleted(int componentTag, CompletedModule module)
            : base(componentTag)
        {
            Module = module;
        }

        public CompletedModule Module { get; }
    }

    public sealed record Rejected : CarouselChange
    {
        internal Rejected(int componentTag, CarouselDefect defect, int? moduleId)
            : base(componentTag)
        {
            Defect = defect;
            ModuleId = moduleId;
        }

        public CarouselDefect Defect { get; }

        public int? ModuleId { get; }
    }

    public sealed record Dropped : CarouselChange
    {
        internal Dropped(int componentTag, CarouselDefect defect)
            : base(componentTag)
        {
            Defect = defect;
        }

        public CarouselDefect Defect { get; }
    }

    public sealed record Unreadable : CarouselChange
    {
        internal Unreadable(int componentTag, TableDefect defect)
            : base(componentTag)
        {
            Defect = defect;
        }

        public TableDefect Defect { get; }
    }
}
