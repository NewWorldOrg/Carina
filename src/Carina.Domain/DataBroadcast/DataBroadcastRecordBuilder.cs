namespace Carina.Domain.DataBroadcast;

/// <summary>
/// Gathers what a <see cref="CarouselState"/> says changed while a recording is read, into the record of
/// its data broadcast: every module version of every download once, when each was last held valid, and every
/// event message. A version put together again with other content takes the place of what was held.
/// </summary>
public sealed class DataBroadcastRecordBuilder
{
    private readonly List<CarouselKey> carousels = [];
    private readonly List<VersionKey> arrivals = [];
    private readonly Dictionary<VersionKey, ModuleVersion> versions = [];
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
                Arrive(arrived.DownloadId, arrived.Module);

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

        return new DataBroadcastRecord(
            startsAt,
            tag,
            [.. carousels.Select(carousel => new RecordedCarousel(
                carousel.Tag,
                carousel.DownloadId,
                [.. arrivals.Where(key => key.Carousel == carousel).Select(key => versions[key])]))],
            events,
            false);
    }

    private void Hold(CarouselCatalog catalog, long at)
    {
        entryTag = catalog.EntryTag;

        foreach (CatalogCarousel carousel in catalog.Carousels)
        {
            CarouselKey listed = Note(carousel.Tag, carousel.DownloadId);

            foreach (CatalogModule module in carousel.Modules.Where(module => module.Arrived))
            {
                SeeAgain(new VersionKey(listed, module.Id, module.Version), at);
            }
        }
    }

    private void Arrive(uint downloadId, ModuleVersion module)
    {
        VersionKey key = new(Note(module.Tag, downloadId), module.ModuleId, module.Version);

        if (versions.TryGetValue(key, out ModuleVersion? held) && held.CarriesTheSameAs(module))
        {
            return;
        }

        versions[key] = module;
        arrivals.Remove(key);
        arrivals.Add(key);
    }

    private CarouselKey Note(int tag, uint downloadId)
    {
        CarouselKey key = new(tag, downloadId);

        if (!carousels.Contains(key))
        {
            carousels.Add(key);
        }

        return key;
    }

    private void SeeAgain(VersionKey key, long at)
    {
        if (versions.TryGetValue(key, out ModuleVersion? held))
        {
            versions[key] = held.SeenAt(at);
        }
    }

    private readonly record struct CarouselKey(int Tag, uint DownloadId);

    private readonly record struct VersionKey(CarouselKey Carousel, int ModuleId, int Version);
}
