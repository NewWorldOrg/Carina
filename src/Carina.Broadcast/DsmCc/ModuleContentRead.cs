namespace Carina.Broadcast.DsmCc;

internal abstract record ModuleContentRead
{
    private ModuleContentRead()
    {
    }

    public sealed record Parsed : ModuleContentRead
    {
        internal Parsed(IReadOnlyList<ModuleResource> resources)
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
