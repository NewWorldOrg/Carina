namespace Carina.Domain.DataBroadcast;

public sealed class DataBroadcastRecordBuilder(long mostBytes = DataBroadcastRecord.MostBytes)
{
    private readonly List<CarouselKey> carousels = [];
    private readonly List<VersionKey> arrivals = [];
    private readonly Dictionary<VersionKey, ModuleVersion> versions = [];
    private readonly HashSet<VersionKey> valid = [];
    private readonly List<EventMessage> events = [];
    private readonly HashSet<VersionKey> letGo = [];

    private int? entryTag;
    private bool autoStart;
    private bool incomplete;
    private long versionBytes;
    private long eventBytes;
    private long? lastAt;

    public long Bytes
        => DataBroadcastRecord.HeaderBytes
           + ((long)carousels.Count * RecordedCarousel.HeaderBytes)
           + versionBytes
           + DataBroadcastRecord.EventCountBytes
           + eventBytes;

    public void Take(CarouselDelta delta, long at)
    {
        ArgumentNullException.ThrowIfNull(delta);

        if (lastAt is { } before && at > before)
        {
            LetGoOfWhatIsOver();
        }

        lastAt = lastAt is { } seen && seen > at ? seen : at;

        switch (delta)
        {
            case CarouselDelta.CatalogChanged changed:
                Hold(changed.Catalog, at);

                break;
            case CarouselDelta.ModuleArrived arrived:
                Arrive(arrived.DownloadId, arrived.Module, at);

                break;
            case CarouselDelta.EventCame came:
                events.Add(came.Message);
                eventBytes += came.Message.Bytes;

                break;
            case CarouselDelta.NptReferred:
                break;
            case CarouselDelta.CarouselDropped dropped:
                incomplete = true;
                valid.RemoveWhere(key => key.Carousel.Tag == dropped.Tag);

                break;
            case CarouselDelta.Absent:
                valid.Clear();

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(delta), delta, "A change is one of the kinds named.");
        }
    }

    public DataBroadcastRecord? Build(long startsAt, long endsAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(endsAt, startsAt);

        if (entryTag is not { } tag)
        {
            return null;
        }

        LetGoOfWhatIsOver();

        foreach (VersionKey key in valid)
        {
            SeeAgain(key, endsAt);
        }

        return new DataBroadcastRecord(
            startsAt,
            tag,
            [.. carousels.Select(carousel => new RecordedCarousel(
                carousel.Tag,
                carousel.DownloadId,
                [.. arrivals.Where(key => key.Carousel == carousel).Select(key => versions[key])]))],
            events,
            incomplete,
            autoStart);
    }

    private void Hold(CarouselCatalog catalog, long at)
    {
        entryTag = catalog.EntryTag;
        autoStart = catalog.AutoStart;
        valid.Clear();

        foreach (CatalogCarousel carousel in catalog.Carousels)
        {
            CarouselKey listed = Note(carousel.Tag, carousel.DownloadId);

            foreach (CatalogModule module in carousel.Modules.Where(module => module.Arrived))
            {
                VersionKey key = new(listed, module.Id, module.Version);

                SeeAgain(key, at);
                valid.Add(key);
            }
        }
    }

    private void Arrive(uint downloadId, ModuleVersion module, long at)
    {
        VersionKey key = new(Note(module.Tag, downloadId), module.ModuleId, module.Version);

        if (!letGo.Contains(key))
        {
            Keep(key, module);
        }

        if (entryTag is null)
        {
            return;
        }

        foreach (VersionKey held in valid)
        {
            SeeAgain(held, at);
        }
    }

    private void Keep(VersionKey key, ModuleVersion module)
    {
        valid.Add(key);

        if (versions.TryGetValue(key, out ModuleVersion? held) && held.CarriesTheSameAs(module))
        {
            return;
        }

        versionBytes += module.Bytes - (held?.Bytes ?? 0);
        versions[key] = module;
        arrivals.Remove(key);
        arrivals.Add(key);
    }

    private void LetGoOfWhatIsOver()
    {
        if (entryTag is not { } tag || (Bytes <= mostBytes && arrivals.Count <= VersionTrim.MostVersionsInACarousel))
        {
            return;
        }

        IReadOnlyList<HeldVersion> leftOut = VersionTrim.LeftOut(
            [.. arrivals.Select(key => new HeldVersion(key.Carousel.Tag, key.Carousel.DownloadId, versions[key]))],
            tag,
            Bytes,
            mostBytes);

        foreach (HeldVersion version in leftOut)
        {
            VersionKey key = new(new CarouselKey(version.Tag, version.DownloadId), version.Version.ModuleId, version.Version.Version);

            versions.Remove(key);
            arrivals.Remove(key);
            valid.Remove(key);
            letGo.Add(key);
            versionBytes -= version.Version.Bytes;
            incomplete = true;
        }
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
