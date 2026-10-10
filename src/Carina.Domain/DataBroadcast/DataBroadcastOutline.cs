namespace Carina.Domain.DataBroadcast;

/// <summary>
/// One version of a module told without its resources: its id and version, when it was first and last seen, and the
/// bytes its resources take.
/// </summary>
public sealed record OutlinedVersion
{
    public OutlinedVersion(int moduleId, int version, long firstSeen, long lastSeen, long entityBytes)
    {
        if (lastSeen < firstSeen)
        {
            throw new ArgumentOutOfRangeException(nameof(lastSeen), lastSeen, "A version is last seen no earlier than it is first seen.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entityBytes);

        ModuleId = CarouselNumbers.ModuleId(moduleId, nameof(moduleId));
        Version = CarouselNumbers.Version(version, nameof(version));
        FirstSeen = firstSeen;
        LastSeen = lastSeen;
        EntityBytes = entityBytes;
    }

    public int ModuleId { get; }

    public int Version { get; }

    public long FirstSeen { get; }

    public long LastSeen { get; }

    public TimeSpan FirstSeenAt => StreamClock.ToTime(FirstSeen);

    public TimeSpan LastSeenAt => StreamClock.ToTime(LastSeen);

    public long EntityBytes { get; }
}

/// <summary>
/// One download of a carousel told without the resources of its versions, the versions in the order they were first
/// seen.
/// </summary>
public sealed record OutlinedCarousel
{
    public OutlinedCarousel(int tag, uint downloadId, IReadOnlyList<OutlinedVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        if (versions.Select(version => (version.ModuleId, version.Version)).Distinct().Count() != versions.Count)
        {
            throw new ArgumentException("A carousel holds each version of a module once.", nameof(versions));
        }

        Tag = CarouselNumbers.Tag(tag, nameof(tag));
        DownloadId = downloadId;
        Versions = [.. versions.OrderBy(version => version.FirstSeen)];
    }

    public int Tag { get; }

    public uint DownloadId { get; }

    public IReadOnlyList<OutlinedVersion> Versions { get; }
}

/// <summary>
/// A <see cref="DataBroadcastRecord"/> told without the resources of its versions: all a catalog of it needs.
/// </summary>
public sealed class DataBroadcastOutline
{
    public DataBroadcastOutline(
        long startsAt,
        int entryTag,
        IReadOnlyList<OutlinedCarousel> carousels,
        IReadOnlyList<EventMessage> events,
        bool incomplete,
        bool autoStart)
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

    public bool AutoStart { get; }

    public string StartupDocument => CarouselCatalog.StartupDocumentOf(EntryTag);

    public bool Incomplete { get; }

    public IReadOnlyList<OutlinedCarousel> Carousels { get; }

    public IReadOnlyList<EventMessage> Events { get; }
}
