namespace Carina.Domain.Segments;

public sealed record CatalogEstimate(CatalogKind Kind, double? Confidence, CatalogDecision? DecidedBy)
{
    public static readonly CatalogEstimate Unknown = new(CatalogKind.Unknown, null, null);
}

/// <summary>
/// What the stretches of a catalog entry say of it: its kind, the series and stations its whole
/// stretches are spread over, and what each stretch alone is taken for, in the order the stretches
/// were handed over.
/// </summary>
public sealed record CatalogPattern(CatalogEstimate Estimate, int SeriesCount, int StationCount, IReadOnlyList<SegmentKind?> Overrides);

/// <summary>
/// The kind of a catalog entry, from the way its stretches are spread, the first row that holds:
/// an answer given on one of its stretches decides outright; a length on the 15-second grid
/// (<see cref="OnTheGrid"/>, a second and a half short of a multiple to half a second over) heard in two series or on two stations is an advert, 0.9; a piece
/// shorter than 30 seconds heard at about the same place (<see cref="SamePlaceWithin"/>) from the
/// start of two episodes of one series is a fixed part of the programme, 0.7; a piece shorter than
/// 15 seconds heard within 15 seconds of the start or end of programmes of two series is a fixed part
/// of the station's output, 0.6; a length on the grid of up to a minute heard in one series is an advert,
/// 0.5, since an opening or ending repeated within a series is often a whole slot of a minute and a half;
/// anything else is not known. The rows for openings and endings are not applied: their sound falls to the rows
/// below. Only the stretches that cover the entry whole (<see cref="WholeWithin"/>) count; a shorter
/// one is taken on its own, and when the entry is an opening or an ending, a shorter one on the grid
/// heard outside the entry's own series is an advert there, the song played in another series'
/// break.
/// </summary>
public static class CatalogKinds
{
    public static readonly TimeSpan Grid = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan ShortOfTheGrid = TimeSpan.FromSeconds(1.5);

    public static readonly TimeSpan OverTheGrid = TimeSpan.FromSeconds(0.5);

    public static readonly TimeSpan WholeWithin = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan SamePlaceWithin = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan ProgrammeFixtureShorterThan = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan EdgeWithin = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan StationFixtureShorterThan = TimeSpan.FromSeconds(15);

    private const int FewestOnTheGrid = 1;

    private const int MostOnTheGrid = 8;

    private const int MostInOneSeries = 4;

    private const double Certain = 1.0;

    private const double AcrossSeries = 0.9;

    private const double SamePlace = 0.7;

    private const double AtTheEdges = 0.6;

    private const double OneSeries = 0.5;

    public static CatalogPattern Estimate(TimeSpan length, IReadOnlyList<PlacedOccurrence> occurrences, SeriesLinks links)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(links);

        PlacedOccurrence[] whole = [.. occurrences.Where(placed => IsWhole(placed, length))];
        PlacedOccurrence[] counted = whole.Length > 0 ? whole : [.. occurrences];
        IReadOnlyList<IReadOnlyList<PlacedOccurrence>> series = links.Group(counted, placed => placed.Occurrence.Member);
        int stations = counted.Select(placed => (placed.Occurrence.NetworkId, placed.Occurrence.ServiceId)).Distinct().Count();
        CatalogEstimate estimate = Answered(counted) ?? Spread(length, series, stations);
        IReadOnlyList<PlacedOccurrence> home = series.MaxBy(group => group.Count) ?? [];

        return new CatalogPattern(
            estimate,
            series.Count,
            stations,
            [.. occurrences.Select(placed => Override(placed, length, estimate.Kind, home, links))]);
    }

    /// <summary>
    /// Whether a length is no more than <see cref="ShortOfTheGrid"/> short of a multiple of <see cref="Grid"/>
    /// from one to eight, 15 to 120 seconds, and no more than <see cref="OverTheGrid"/> over it. A match is cut
    /// to the sound it hears, and an advert's slot keeps about half a second of silence at either end, so what
    /// is heard of a slot falls short of the slot rather than over it.
    /// </summary>
    public static bool OnTheGrid(TimeSpan length) => OnTheGrid(length, MostOnTheGrid);

    private static bool OnTheGrid(TimeSpan length, int mostMultiple)
    {
        long multiple = (long)Math.Round((length + ((ShortOfTheGrid - OverTheGrid) / 2)) / Grid, MidpointRounding.AwayFromZero);
        TimeSpan slot = Grid * multiple;

        return multiple >= FewestOnTheGrid && multiple <= mostMultiple && length >= slot - ShortOfTheGrid && length <= slot + OverTheGrid;
    }

    private static bool IsWhole(PlacedOccurrence placed, TimeSpan length) => placed.Occurrence.Length >= length - WholeWithin;

    private static CatalogEstimate? Answered(IEnumerable<PlacedOccurrence> counted)
    {
        SegmentKind[] answers = [.. counted.Select(placed => placed.Answer).OfType<SegmentKind>()];

        if (answers.Length is 0)
        {
            return null;
        }

        SegmentKind most = answers.GroupBy(answer => answer).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).First().Key;

        return new CatalogEstimate(KindOf(most), Certain, CatalogDecision.Answer);
    }

    private static CatalogKind KindOf(SegmentKind answer) => answer switch
    {
        SegmentKind.Break => CatalogKind.Cm,
        SegmentKind.Opening => CatalogKind.Opening,
        SegmentKind.Ending => CatalogKind.Ending,
        _ => CatalogKind.ProgrammeFixture,
    };

    private static CatalogEstimate Spread(TimeSpan length, IReadOnlyList<IReadOnlyList<PlacedOccurrence>> series, int stations)
    {
        if (OnTheGrid(length) && (series.Count >= 2 || stations >= 2))
        {
            return new CatalogEstimate(CatalogKind.Cm, AcrossSeries, CatalogDecision.Pattern);
        }

        if (length < ProgrammeFixtureShorterThan && series.Any(HeardTwiceInOnePlace))
        {
            return new CatalogEstimate(CatalogKind.ProgrammeFixture, SamePlace, CatalogDecision.Pattern);
        }

        if (length < StationFixtureShorterThan && series.Count(group => group.Any(AtAnEdge)) >= 2)
        {
            return new CatalogEstimate(CatalogKind.StationFixture, AtTheEdges, CatalogDecision.Pattern);
        }

        return OnTheGrid(length, MostInOneSeries) && series.Count == 1
            ? new CatalogEstimate(CatalogKind.Cm, OneSeries, CatalogDecision.Pattern)
            : CatalogEstimate.Unknown;
    }

    private static bool HeardTwiceInOnePlace(IReadOnlyList<PlacedOccurrence> series)
        => series.Any(one => series.Any(other => SamePlaceInAnotherEpisode(one, other)));

    private static bool SamePlaceInAnotherEpisode(PlacedOccurrence one, PlacedOccurrence other)
        => !one.Occurrence.RecordingId.Equals(other.Occurrence.RecordingId)
           && one.IntoProgramme is TimeSpan here
           && other.IntoProgramme is TimeSpan there
           && (here - there).Duration() <= SamePlaceWithin;

    private static bool AtAnEdge(PlacedOccurrence placed)
        => Near(placed.Occurrence, placed.ProgrammeStarts) || Near(placed.Occurrence, placed.ProgrammeEnds);

    private static bool Near(CatalogOccurrence occurrence, TimeSpan? edge)
        => edge is TimeSpan at && occurrence.StartsAt >= at - EdgeWithin && occurrence.EndsAt <= at + EdgeWithin;

    private static SegmentKind? Override(PlacedOccurrence placed, TimeSpan length, CatalogKind kind, IReadOnlyList<PlacedOccurrence> home, SeriesLinks links)
    {
        if (placed.Answer is SegmentKind answer)
        {
            return answer;
        }

        bool songElsewhere = kind is CatalogKind.Opening or CatalogKind.Ending
                             && !IsWhole(placed, length)
                             && OnTheGrid(placed.Occurrence.Length)
                             && !home.Any(member => links.Link(member.Occurrence.Member, placed.Occurrence.Member));

        return songElsewhere ? SegmentKind.Break : null;
    }
}
