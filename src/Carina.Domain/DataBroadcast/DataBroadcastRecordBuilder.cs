namespace Carina.Domain.DataBroadcast;

/// <summary>
/// Gathers what a <see cref="CarouselState"/> says changed while a recording is read, into the record of
/// its data broadcast: every module version once, when each was last held valid, and every event message.
/// </summary>
public sealed class DataBroadcastRecordBuilder
{
    private readonly SortedDictionary<int, uint> downloads = [];
    private readonly Dictionary<(int Tag, int ModuleId, int Version), ModuleVersion> versions = [];
    private readonly List<EventMessage> events = [];

    private int? entryTag;

    public void Take(CarouselDelta delta, long at)
    {
        ArgumentNullException.ThrowIfNull(delta);

        switch (delta)
        {
            case CarouselDelta.CatalogChanged changed:
                Hold(changed.Catalog, at);

                break;
            case CarouselDelta.ModuleArrived arrived:
                versions.TryAdd((arrived.Module.Tag, arrived.Module.ModuleId, arrived.Module.Version), arrived.Module);

                break;
            case CarouselDelta.EventCame came:
                events.Add(came.Message);

                break;
        }
    }

    /// <summary>
    /// The record as gathered, beginning at <paramref name="startsAt"/>, or nothing when no catalog was ever
    /// read.
    /// </summary>
    public DataBroadcastRecord? Build(long startsAt)
    {
        if (entryTag is not { } tag)
        {
            return null;
        }

        IEnumerable<int> tags = downloads.Keys.Union(versions.Keys.Select(key => key.Tag)).Order();

        return new DataBroadcastRecord(
            startsAt,
            tag,
            [.. tags.Select(carousel => new RecordedCarousel(
                carousel,
                downloads.GetValueOrDefault(carousel),
                [.. versions.Values.Where(version => version.Tag == carousel)]))],
            events,
            false);
    }

    private void Hold(CarouselCatalog catalog, long at)
    {
        entryTag = catalog.EntryTag;

        foreach (CatalogCarousel carousel in catalog.Carousels)
        {
            downloads[carousel.Tag] = carousel.DownloadId;

            foreach (CatalogModule module in carousel.Modules.Where(module => module.Arrived))
            {
                SeeAgain((carousel.Tag, module.Id, module.Version), at);
            }
        }
    }

    private void SeeAgain((int Tag, int ModuleId, int Version) key, long at)
    {
        if (versions.TryGetValue(key, out ModuleVersion? held))
        {
            versions[key] = held.SeenAt(at);
        }
    }
}
