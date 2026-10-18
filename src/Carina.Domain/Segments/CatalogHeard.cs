namespace Carina.Domain.Segments;

/// <summary>
/// A stretch of the recording judged where a catalog entry is heard, the kind it counts as there, and
/// how many stretches, series and stations its entry is heard in. A programme fixture counts as the
/// programme and a station fixture as a break; the kind the stretch alone is taken for wins over its
/// entry's; an entry whose kind is not known counts as nothing.
/// </summary>
public sealed record CatalogHeard
{
    public CatalogHeard(TimeSpan starts, TimeSpan ends, SegmentKind? kind, int occurrences, int series, int stations)
    {
        if (starts < TimeSpan.Zero || ends <= starts)
        {
            throw new ArgumentException("A stretch starts no earlier than its recording's own zero and ends after it starts.", nameof(ends));
        }

        if (kind is SegmentKind given && !Enum.IsDefined(given))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A stretch counts as one of the kinds of segment.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(occurrences);
        ArgumentOutOfRangeException.ThrowIfNegative(series);
        ArgumentOutOfRangeException.ThrowIfNegative(stations);

        Starts = starts;
        Ends = ends;
        Kind = kind;
        Occurrences = occurrences;
        Series = series;
        Stations = stations;
    }

    public TimeSpan Starts { get; }

    public TimeSpan Ends { get; }

    public SegmentKind? Kind { get; }

    public int Occurrences { get; }

    public int Series { get; }

    public int Stations { get; }

    public static CatalogHeard Of(CatalogOccurrence occurrence, CatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(entry);

        if (!occurrence.EntryId.Equals(entry.Id))
        {
            throw new ArgumentException("A stretch is read beside the entry it is a stretch of.", nameof(entry));
        }

        return new CatalogHeard(
            occurrence.StartsAt,
            occurrence.EndsAt,
            occurrence.KindOverride ?? CountsAs(entry.Kind),
            entry.OccurrenceCount,
            entry.SeriesCount,
            entry.StationCount);
    }

    public static SegmentKind? CountsAs(CatalogKind kind) => kind switch
    {
        CatalogKind.Cm or CatalogKind.StationFixture => SegmentKind.Break,
        CatalogKind.ProgrammeFixture => SegmentKind.Programme,
        CatalogKind.Opening => SegmentKind.Opening,
        CatalogKind.Ending => SegmentKind.Ending,
        _ => null,
    };
}
