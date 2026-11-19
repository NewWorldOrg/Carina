namespace Carina.Domain.DataBroadcast;

public sealed class DataBroadcastRecord
{
    public const long MostBytes = 256L * 1024 * 1024;

    public const int HeaderBytes = 8 + sizeof(ushort) + sizeof(long) + sizeof(byte) + sizeof(byte) + sizeof(ushort);

    public const int EventCountBytes = sizeof(uint);

    public DataBroadcastRecord(
        long startsAt,
        int entryTag,
        IReadOnlyList<RecordedCarousel> carousels,
        IReadOnlyList<EventMessage> events,
        bool incomplete,
        bool autoStart = false)
    {
        ArgumentNullException.ThrowIfNull(carousels);
        ArgumentNullException.ThrowIfNull(events);

        if (carousels.Select(carousel => (carousel.Tag, carousel.DownloadId)).Distinct().Count() != carousels.Count)
        {
            throw new ArgumentException("A record holds each download of a carousel once.", nameof(carousels));
        }

        StartsAt = startsAt;
        EntryTag = CarouselNumbers.Tag(entryTag, nameof(entryTag));
        Carousels = [.. carousels.OrderBy(carousel => carousel.Tag)];
        Events = [.. events.OrderBy(message => message.FiresAt)];
        Incomplete = incomplete;
        AutoStart = autoStart;
    }

    public long StartsAt { get; }

    public TimeSpan Start => StreamClock.ToTime(StartsAt);

    public int EntryTag { get; }

    public IReadOnlyList<RecordedCarousel> Carousels { get; }

    public IReadOnlyList<EventMessage> Events { get; }

    public bool Incomplete { get; }

    public bool AutoStart { get; }

    public string StartupDocument => CarouselCatalog.StartupDocumentOf(EntryTag);

    public DataBroadcastOutline Outline
        => new(
            StartsAt,
            EntryTag,
            [.. Carousels.Select(carousel => new OutlinedCarousel(
                carousel.Tag,
                carousel.DownloadId,
                [.. carousel.Versions.Select(version => new OutlinedVersion(
                    version.ModuleId,
                    version.Version,
                    version.FirstSeen,
                    version.LastSeen,
                    version.Bytes - ModuleVersion.HeaderBytes))]))],
            Events,
            Incomplete,
            AutoStart);

    public int Modules
        => Carousels.Sum(carousel => carousel.Versions.Select(version => version.ModuleId).Distinct().Count());

    public long Bytes
        => HeaderBytes
           + Carousels.Sum(carousel => carousel.Bytes)
           + EventCountBytes
           + Events.Sum(message => message.Bytes);

    public IReadOnlyList<ModuleVersion> VersionsAt(long at)
        => [
            .. Carousels
                .SelectMany(carousel => carousel.Versions)
                .Where(version => version.FirstSeen <= at)
                .GroupBy(version => (version.Tag, version.ModuleId))
                .Select(module => module.Aggregate((latest, next) => next.FirstSeen >= latest.FirstSeen ? next : latest))
                .OrderBy(version => version.Tag)
                .ThenBy(version => version.ModuleId),
        ];

    public IReadOnlyList<EventMessage> EventsBetween(long from, long to)
        => [.. Events.Where(message => message.FiresAt >= from && message.FiresAt < to)];

    public DataBroadcastRecord Shifted(long by, long startsAt)
        => new(
            startsAt,
            EntryTag,
            [.. Carousels.Select(carousel => new RecordedCarousel(
                carousel.Tag,
                carousel.DownloadId,
                [.. carousel.Versions.Select(version => version.Shifted(by))]))],
            [.. Events.Select(message => message.Shifted(by))],
            Incomplete,
            AutoStart);

    public DataBroadcastRecord Within(long mostBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mostBytes);

        long bytes = Bytes;
        IReadOnlyList<HeldVersion> leftOut = VersionTrim.LeftOut(Held(), EntryTag, bytes, mostBytes);

        if (leftOut.Count is 0 && bytes <= mostBytes)
        {
            return this;
        }

        HashSet<ModuleVersion> dropped = new(leftOut.Select(version => version.Version), ReferenceEqualityComparer.Instance);

        return new DataBroadcastRecord(
            StartsAt,
            EntryTag,
            [.. Carousels.Select(carousel => new RecordedCarousel(
                carousel.Tag,
                carousel.DownloadId,
                [.. carousel.Versions.Where(version => !dropped.Contains(version))]))],
            Events,
            true,
            AutoStart);
    }

    private List<HeldVersion> Held()
        => [.. Carousels.SelectMany(carousel => carousel.Versions.Select(version => new HeldVersion(carousel.Tag, carousel.DownloadId, version)))];
}
