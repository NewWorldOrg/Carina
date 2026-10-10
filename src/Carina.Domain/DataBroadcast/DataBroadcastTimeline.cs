namespace Carina.Domain.DataBroadcast;

/// <summary>
/// One module version placed on a source a recording is played from: the seconds of that source it runs from and
/// to, and the version itself.
/// </summary>
public sealed record PlacedVersion(TimeSpan From, TimeSpan To, ModuleVersion Module);

/// <summary>
/// One download of a carousel with its versions placed on a source, in the order they were first seen.
/// </summary>
public sealed record PlacedCarousel(int Tag, uint DownloadId, IReadOnlyList<PlacedVersion> Versions);

/// <summary>
/// One event message placed at the second of a source it fires at.
/// </summary>
public sealed record PlacedEvent(TimeSpan At, EventMessage Message);

/// <summary>
/// The data broadcast of a recording placed on a source it is played from: where it is entered, whether the
/// broadcaster asks for it to open by itself, the document it opens on, whether versions were left out of the record,
/// every version of every carousel with the seconds of the source it runs over, and every event message at the
/// second of the source it fires at, each counted from the source's own zero.
/// </summary>
public sealed record DataBroadcastTimeline
{
    private DataBroadcastTimeline(DataBroadcastRecord record, IReadOnlyList<PlacedCarousel> carousels, IReadOnlyList<PlacedEvent> events)
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

    /// <summary>
    /// Places a record on a source whose zero is <paramref name="shift"/> on the file's own clock and which lasts
    /// <paramref name="length"/> where it says so, from <paramref name="from"/> on: every version still running at or
    /// after it, one that began before the source's zero running from that zero and one running past the source's
    /// end running to it, and every event message firing at or after it. A version first seen, or an event message
    /// firing, after the source's end is left out.
    /// </summary>
    public static DataBroadcastTimeline Of(DataBroadcastRecord record, TimeSpan shift, TimeSpan? length, TimeSpan from)
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

    private static PlacedVersion? Placed(ModuleVersion version, TimeSpan shift, TimeSpan? length, TimeSpan from)
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
