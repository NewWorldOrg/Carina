namespace Carina.Domain.Segments;

/// <summary>
/// A stretch of the recording judged where a catalog entry is heard, the kind it counts as there, how
/// many stretches, series and stations its entry is heard in, how sure its entry's kind is, when it
/// is known, and which entry it is, when that is said. A programme fixture counts as the programme and a station fixture as a break; the kind the
/// stretch alone is taken for wins over its entry's; an entry whose kind is not known counts as nothing, and so does a stretch of an
/// opening's or an ending's sound taken for the programme by where it lies, since the song heard there may be sung in the programme or
/// be advertised between adverts, which the stretch alone does not tell.
/// </summary>
public sealed record CatalogHeard
{
    public CatalogHeard(
        TimeSpan starts,
        TimeSpan ends,
        SegmentKind? kind,
        int occurrences,
        int series,
        int stations,
        double? confidence = null,
        CatalogEntryId? entry = null)
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

        if (confidence is double sure && !(sure is >= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "A confidence lies from 0 to 1.");
        }

        Starts = starts;
        Ends = ends;
        Kind = kind;
        Occurrences = occurrences;
        Series = series;
        Stations = stations;
        Confidence = confidence;
        Entry = entry;
    }

    public TimeSpan Starts { get; }

    public TimeSpan Ends { get; }

    public SegmentKind? Kind { get; }

    public int Occurrences { get; }

    public int Series { get; }

    public int Stations { get; }

    public double? Confidence { get; }

    public CatalogEntryId? Entry { get; }

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
            KindOf(occurrence.KindOverride, entry.Kind),
            entry.OccurrenceCount,
            entry.SeriesCount,
            entry.StationCount,
            entry.Confidence,
            entry.Id);
    }

    /// <summary>
    /// The kind a stretch counts as when it alone is taken for <paramref name="alone"/>, if anything, and its entry is of
    /// <paramref name="kind"/>: what it alone is taken for, but nothing when that is the programme and its entry an
    /// opening or an ending, or else what its entry counts as.
    /// </summary>
    public static SegmentKind? KindOf(SegmentKind? alone, CatalogKind kind)
        => alone is SegmentKind.Programme && CountsAs(kind) is SegmentKind.Opening or SegmentKind.Ending ? null : alone ?? CountsAs(kind);

    public static SegmentKind? CountsAs(CatalogKind kind) => kind switch
    {
        CatalogKind.Cm or CatalogKind.StationFixture => SegmentKind.Break,
        CatalogKind.ProgrammeFixture => SegmentKind.Programme,
        CatalogKind.Opening => SegmentKind.Opening,
        CatalogKind.Ending => SegmentKind.Ending,
        _ => null,
    };
}
