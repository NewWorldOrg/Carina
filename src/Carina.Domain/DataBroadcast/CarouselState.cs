namespace Carina.Domain.DataBroadcast;

/// <summary>
/// The data broadcast of one live channel or one recording as it stands: whether the service carries one,
/// the catalog, and every module version that is valid and has arrived. Each signal read of the stream moves
/// it on and says what changed, at the moment it was read on the <see cref="StreamClock"/>, never a raw PTS.
/// </summary>
public sealed class CarouselState
{
    private static readonly IReadOnlyList<CarouselDelta> Nothing = [];

    private readonly SortedDictionary<int, Carousel> carousels = [];
    private readonly HashSet<(int Tag, CarouselDropReason Reason)> dropped = [];

    private DataBroadcastEntry? entry;

    private IReadOnlyList<int> tags = [];

    public bool IsAbsent { get; private set; }

    public CarouselCatalog? Catalog => entry is null ? null : CatalogOf(entry);

    public IReadOnlyList<ModuleVersion> Modules
        => [.. carousels.Values.SelectMany(carousel => carousel.ArrivedInListedOrder())];

    public IReadOnlyList<CarouselDelta> Apply(CarouselSignal signal, long at)
    {
        ArgumentNullException.ThrowIfNull(signal);

        return signal switch
        {
            CarouselSignal.Carried carried => Carry(carried.Entry, carried.Tags),
            CarouselSignal.NotCarried => Withdraw(),
            CarouselSignal.CatalogUpdated updated => List(updated, at),
            CarouselSignal.ModuleCompleted completed => Complete(completed, at),
            CarouselSignal.EventTimed timed => Fire(timed.Message),
            CarouselSignal.Dropped drop => Drop(drop.Tag, drop.Reason),
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, "A signal is one of the kinds named."),
        };
    }

    private IReadOnlyList<CarouselDelta> Carry(DataBroadcastEntry carried, IReadOnlyList<int> listed)
    {
        if (carried == entry && listed.SequenceEqual(tags))
        {
            return Nothing;
        }

        entry = carried;
        tags = listed;
        IsAbsent = false;

        foreach (int gone in carousels.Keys.Where(tag => !listed.Contains(tag)).ToArray())
        {
            carousels.Remove(gone);
        }

        dropped.RemoveWhere(seen => !listed.Contains(seen.Tag));

        return Changed();
    }

    private IReadOnlyList<CarouselDelta> Withdraw()
    {
        if (IsAbsent)
        {
            return Nothing;
        }

        carousels.Clear();
        dropped.Clear();
        entry = null;
        IsAbsent = true;

        return [new CarouselDelta.Absent()];
    }

    private IReadOnlyList<CarouselDelta> List(CarouselSignal.CatalogUpdated updated, long at)
    {
        if (IsAbsent || (entry is not null && !tags.Contains(updated.Tag)))
        {
            return Nothing;
        }

        Carousel listed = new(updated.Tag, updated.DownloadId, updated.Modules);

        if (carousels.TryGetValue(updated.Tag, out Carousel? before) && before.DownloadId == updated.DownloadId)
        {
            listed.KeepFrom(before, updated.Superseded, at);
        }

        carousels[updated.Tag] = listed;
        dropped.RemoveWhere(seen => seen.Tag == updated.Tag);

        return Changed();
    }

    private IReadOnlyList<CarouselDelta> Complete(CarouselSignal.ModuleCompleted completed, long at)
    {
        if (!carousels.TryGetValue(completed.Tag, out Carousel? carousel) || !carousel.Awaits(completed.ModuleId, completed.Version))
        {
            return Nothing;
        }

        ModuleVersion arrived = new(completed.Tag, completed.ModuleId, completed.Version, at, at, completed.Resources);

        carousel.Arrived[completed.ModuleId] = arrived;

        return [new CarouselDelta.ModuleArrived(carousel.DownloadId, arrived), .. Changed()];
    }

    private IReadOnlyList<CarouselDelta> Fire(EventMessage message)
        => entry is null ? Nothing : [new CarouselDelta.EventCame(message)];

    private IReadOnlyList<CarouselDelta> Drop(int tag, CarouselDropReason reason)
    {
        if (IsAbsent)
        {
            return Nothing;
        }

        bool removed = carousels.Remove(tag);
        List<CarouselDelta> deltas = [];

        if (dropped.Add((tag, reason)))
        {
            deltas.Add(new CarouselDelta.CarouselDropped(tag, reason));
        }

        if (removed)
        {
            deltas.AddRange(Changed());
        }

        return deltas;
    }

    private IReadOnlyList<CarouselDelta> Changed()
        => entry is null ? Nothing : [new CarouselDelta.CatalogChanged(CatalogOf(entry))];

    private CarouselCatalog CatalogOf(DataBroadcastEntry of)
        => new(of.Service, of.EntryTag, of.AutoStart, [.. carousels.Values.Select(carousel => carousel.ToCatalog())]);

    private sealed class Carousel
    {
        public Carousel(int tag, uint downloadId, IReadOnlyList<ListedModule> listed)
        {
            Tag = tag;
            DownloadId = downloadId;
            Listed = [.. listed];
        }

        public int Tag { get; }

        public uint DownloadId { get; }

        public IReadOnlyList<ListedModule> Listed { get; }

        public Dictionary<int, ModuleVersion> Arrived { get; } = [];

        public bool Awaits(int moduleId, int version)
            => !Arrived.ContainsKey(moduleId) && Listed.Any(module => module.Id == moduleId && module.Version == version);

        public void KeepFrom(Carousel before, IReadOnlyList<int> superseded, long at)
        {
            foreach (ListedModule module in Listed.Where(module => !superseded.Contains(module.Id)))
            {
                if (before.Arrived.TryGetValue(module.Id, out ModuleVersion? held) && held.Version == module.Version)
                {
                    Arrived[module.Id] = held.SeenAt(at);
                }
            }
        }

        public IEnumerable<ModuleVersion> ArrivedInListedOrder()
            => Listed.Where(module => Arrived.ContainsKey(module.Id)).Select(module => Arrived[module.Id]);

        public CatalogCarousel ToCatalog()
            => new(Tag, DownloadId, [.. Listed.Select(CatalogModuleOf)]);

        private CatalogModule CatalogModuleOf(ListedModule module)
        {
            if (!Arrived.TryGetValue(module.Id, out ModuleVersion? held))
            {
                return new CatalogModule(module.Id, module.Version, module.Size, false, []);
            }

            return new CatalogModule(
                module.Id,
                module.Version,
                module.Size,
                true,
                [.. held.Resources.Select(resource => new CatalogResource(resource.Path, resource.MediaType))]);
        }
    }
}
