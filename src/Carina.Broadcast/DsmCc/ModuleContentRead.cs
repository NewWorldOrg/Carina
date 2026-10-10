namespace Carina.Broadcast.DsmCc;

public abstract record ModuleContentRead
{
    private ModuleContentRead()
    {
    }

    public sealed record Opened : ModuleContentRead
    {
        internal Opened(IReadOnlyList<ModuleResource> resources)
        {
            Resources = resources;
        }

        public IReadOnlyList<ModuleResource> Resources { get; }
    }

    public sealed record Rejected : ModuleContentRead
    {
        internal Rejected(CarouselDefect defect)
        {
            Defect = defect;
        }

        public CarouselDefect Defect { get; }
    }
}
