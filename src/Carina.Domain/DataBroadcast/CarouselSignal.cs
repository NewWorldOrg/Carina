namespace Carina.Domain.DataBroadcast;

/// <summary>
/// What is read of a data broadcast, handed to <see cref="CarouselState"/>: the programme map carrying one
/// or not, a carousel's download info, a module put together, an event message timed, or a carousel left
/// out for being too large.
/// </summary>
public abstract record CarouselSignal
{
    private CarouselSignal()
    {
    }

    public sealed record Carried : CarouselSignal
    {
        public Carried(DataBroadcastEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            Entry = entry;
        }

        public DataBroadcastEntry Entry { get; }
    }

    public sealed record NotCarried : CarouselSignal;

    public sealed record CatalogUpdated : CarouselSignal
    {
        public CatalogUpdated(int tag, uint downloadId, IReadOnlyList<ListedModule> modules, IReadOnlyList<int> superseded)
        {
            ArgumentNullException.ThrowIfNull(modules);
            ArgumentNullException.ThrowIfNull(superseded);

            if (modules.Select(module => module.Id).Distinct().Count() != modules.Count)
            {
                throw new ArgumentException("A carousel lists each module once.", nameof(modules));
            }

            Tag = CarouselNumbers.Tag(tag, nameof(tag));
            DownloadId = downloadId;
            Modules = modules;
            Superseded = superseded;
        }

        public int Tag { get; }

        public uint DownloadId { get; }

        public IReadOnlyList<ListedModule> Modules { get; }

        public IReadOnlyList<int> Superseded { get; }
    }

    public sealed record ModuleCompleted : CarouselSignal
    {
        public ModuleCompleted(int tag, int moduleId, int version, IReadOnlyList<CarouselResource> resources)
        {
            ArgumentNullException.ThrowIfNull(resources);

            Tag = CarouselNumbers.Tag(tag, nameof(tag));
            ModuleId = CarouselNumbers.ModuleId(moduleId, nameof(moduleId));
            Version = CarouselNumbers.Version(version, nameof(version));
            Resources = resources;
        }

        public int Tag { get; }

        public int ModuleId { get; }

        public int Version { get; }

        public IReadOnlyList<CarouselResource> Resources { get; }
    }

    public sealed record EventTimed : CarouselSignal
    {
        public EventTimed(EventMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            Message = message;
        }

        public EventMessage Message { get; }
    }

    public sealed record Dropped : CarouselSignal
    {
        public Dropped(int tag, CarouselDropReason reason)
        {
            if (!Enum.IsDefined(reason))
            {
                throw new ArgumentOutOfRangeException(nameof(reason), reason, "A carousel is left out for one of the reasons named.");
            }

            Tag = CarouselNumbers.Tag(tag, nameof(tag));
            Reason = reason;
        }

        public int Tag { get; }

        public CarouselDropReason Reason { get; }
    }
}
