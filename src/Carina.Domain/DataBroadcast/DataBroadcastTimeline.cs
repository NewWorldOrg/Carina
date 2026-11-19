namespace Carina.Domain.DataBroadcast;

public sealed record PlacedVersion(TimeSpan From, TimeSpan To, OutlinedVersion Module);

public sealed record PlacedCarousel(int Tag, uint DownloadId, IReadOnlyList<PlacedVersion> Versions);

public sealed record PlacedEvent(TimeSpan At, EventMessage Message);

public sealed record DataBroadcastTimeline
{
    private DataBroadcastTimeline(DataBroadcastOutline record, IReadOnlyList<PlacedCarousel> carousels, IReadOnlyList<PlacedEvent> events)
    {
        EntryTag = record.EntryTag;
        AutoStart = record.AutoStart;
        StartupDocument = record.StartupDocument;
        Incomplete = record.Incomplete;
        Carousels = carousels;
        Events = events;
    }

    public int EntryTag { get; }

    public bool AutoStart { get; }

    public string StartupDocument { get; }

    public bool Incomplete { get; }

    public IReadOnlyList<PlacedCarousel> Carousels { get; }

    public IReadOnlyList<PlacedEvent> Events { get; }

    public static DataBroadcastTimeline Of(DataBroadcastOutline record, TimeSpan shift, TimeSpan? length, TimeSpan from)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentOutOfRangeException.ThrowIfLessThan(from, TimeSpan.Zero);

        return new DataBroadcastTimeline(
            record,
            [.. record.Carousels.Select(carousel => new PlacedCarousel(
                carousel.Tag,
                carousel.DownloadId,
                [.. carousel.Versions.Select(version => Placed(version, shift, length, from)).OfType<PlacedVersion>()]))],
            [
                .. record.Events
                    .Select(message => new PlacedEvent(message.At - shift, message))
                    .Where(placed => placed.At >= from && Within(placed.At, length)),
            ]);
    }

    private static PlacedVersion? Placed(OutlinedVersion version, TimeSpan shift, TimeSpan? length, TimeSpan from)
    {
        TimeSpan first = version.FirstSeenAt - shift;
        TimeSpan last = version.LastSeenAt - shift;
        TimeSpan to = length is { } lasts && last > lasts ? lasts : last;

        if (to < from || !Within(first, length))
        {
            return null;
        }

        return new PlacedVersion(first > TimeSpan.Zero ? first : TimeSpan.Zero, to, version);
    }

    private static bool Within(TimeSpan at, TimeSpan? length) => length is not { } lasts || at <= lasts;
}
