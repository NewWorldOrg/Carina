namespace Carina.Domain.DataBroadcast;

/// <summary>
/// The data broadcast taken from one recording, every time told on the recording's own
/// <see cref="StreamClock"/>: where that clock begins, the carousel it is entered from,
/// every module version each carousel carried with when it was first and last seen, every event message in
/// the order they fire, and whether versions were left out to stay within the size a record may take.
/// </summary>
public sealed class DataBroadcastRecord
{
    public const long MostBytes = 256L * 1024 * 1024;

    public DataBroadcastRecord(
        long startsAt,
        int entryTag,
        IReadOnlyList<RecordedCarousel> carousels,
        IReadOnlyList<EventMessage> events,
        bool incomplete)
    {
        ArgumentNullException.ThrowIfNull(carousels);
        ArgumentNullException.ThrowIfNull(events);

        if (carousels.Select(carousel => carousel.Tag).Distinct().Count() != carousels.Count)
        {
            throw new ArgumentException("A record holds each carousel once.", nameof(carousels));
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

    public long Bytes => Carousels.Sum(carousel => carousel.Versions.Sum(version => version.Bytes));

    /// <summary>
    /// For each module, the version first seen latest at or before <paramref name="at"/>, in order of carousel
    /// and module.
    /// </summary>
    public IReadOnlyList<ModuleVersion> VersionsAt(long at)
        => [
            .. Carousels
                .SelectMany(carousel => carousel.Versions)
                .Where(version => version.FirstSeen <= at)
                .GroupBy(version => (version.Tag, version.ModuleId))
                .Select(module => module.MaxBy(version => version.FirstSeen)!)
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
    /// This record within <paramref name="mostBytes"/>, leaving out the versions first seen earliest until
    /// what is left fits, and marked incomplete when anything was left out.
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

        foreach (ModuleVersion version in Oldest())
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

    private IEnumerable<ModuleVersion> Oldest()
        => Carousels
            .SelectMany(carousel => carousel.Versions)
            .OrderBy(version => version.FirstSeen)
            .ThenBy(version => version.Tag)
            .ThenBy(version => version.ModuleId);
}
