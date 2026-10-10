namespace Carina.Domain.DataBroadcast;

/// <summary>
/// The data broadcast taken from one recording, every time told on the recording's own
/// <see cref="StreamClock"/>: where that clock begins, the carousel it is entered from,
/// every module version each download of each carousel carried with when it was first and last seen, in the
/// order the downloads were first read, every event message in
/// the order they fire, and whether versions were left out to stay within the size a record may take.
/// </summary>
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
        bool incomplete)
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
    }

    public long StartsAt { get; }

    public TimeSpan Start => StreamClock.ToTime(StartsAt);

    public int EntryTag { get; }

    public IReadOnlyList<RecordedCarousel> Carousels { get; }

    public IReadOnlyList<EventMessage> Events { get; }

    public bool Incomplete { get; }

    public int Modules
        => Carousels.Sum(carousel => carousel.Versions.Select(version => version.ModuleId).Distinct().Count());

    /// <summary>
    /// The bytes it takes: its header, every carousel with its versions, and every event message.
    /// </summary>
    public long Bytes
        => HeaderBytes
           + Carousels.Sum(carousel => carousel.Bytes)
           + EventCountBytes
           + Events.Sum(message => message.Bytes);

    /// <summary>
    /// For each module, the version first seen latest at or before <paramref name="at"/>, and of versions first
    /// seen at the same moment the one that arrived last, in order of carousel and module.
    /// </summary>
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

    /// <summary>
    /// The event messages that fire from <paramref name="from"/> up to but not including <paramref name="to"/>,
    /// in the order they fire.
    /// </summary>
    public IReadOnlyList<EventMessage> EventsBetween(long from, long to)
        => [.. Events.Where(message => message.FiresAt >= from && message.FiresAt < to)];

    /// <summary>
    /// This record within <paramref name="mostBytes"/>: versions that a later version of the same module took the
    /// place of are left out, first seen earliest first, until what is left fits. The latest version of every
    /// module and every version of the startup document stay even when the record still does not fit, and it is
    /// marked incomplete whenever it was over.
    /// </summary>
    public DataBroadcastRecord Within(long mostBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mostBytes);

        long over = Bytes - mostBytes;

        if (over <= 0)
        {
            return this;
        }

        HashSet<ModuleVersion> leftOut = new(ReferenceEqualityComparer.Instance);

        foreach (ModuleVersion version in Superseded())
        {
            if (over <= 0)
            {
                break;
            }

            leftOut.Add(version);
            over -= version.Bytes;
        }

        return new DataBroadcastRecord(
            StartsAt,
            EntryTag,
            [.. Carousels.Select(carousel => new RecordedCarousel(
                carousel.Tag,
                carousel.DownloadId,
                [.. carousel.Versions.Where(version => !leftOut.Contains(version))]))],
            Events,
            true);
    }

    private IEnumerable<ModuleVersion> Superseded()
    {
        HashSet<ModuleVersion> latest = new(VersionsAt(long.MaxValue), ReferenceEqualityComparer.Instance);

        return Carousels
            .SelectMany(carousel => carousel.Versions)
            .Where(version => !latest.Contains(version) && !IsStartupDocument(version))
            .OrderBy(version => version.FirstSeen);
    }

    private bool IsStartupDocument(ModuleVersion version)
        => version.Tag == EntryTag && version.ModuleId == CarouselCatalog.StartupModuleId;
}
