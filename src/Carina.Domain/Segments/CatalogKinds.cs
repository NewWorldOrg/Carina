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
/// The kind of a catalog entry, from the way its stretches are spread, the first row that holds: an
/// answer of a break, an opening or an ending given on one of its stretches decides outright, and
/// answers of the programme alone leave only the rows of the opening, the ending and the programme; a
/// length on the 15-second grid (<see cref="OnTheGrid"/>, a second and a half short of a multiple to half
/// a second over) heard in two series or on two stations is an advert, 0.9; a piece of
/// <see cref="ThemeShortest"/> to <see cref="ThemeLongest"/> heard in two episodes of one series in the
/// first half of the programme is its opening, 0.8, and in the second half its ending, 0.8, whichever half
/// more of those episodes hear it in, the opening on a tie; a piece shorter than 30 seconds heard at about
/// the same place (<see cref="SamePlaceWithin"/>) from the start of two episodes of one series is a fixed
/// part of the programme, 0.7; a piece shorter than 15 seconds heard within 15 seconds of the start or end
/// of programmes of two series is a fixed part of the station's output, 0.6; a length on the grid of up to
/// a minute heard in one series is an advert, 0.5, since an opening or ending is often a whole slot of a
/// minute and a half; anything else is not known. Only the stretches that cover the entry whole
/// (<see cref="WholeWithin"/>) count; a shorter one is taken on its own.
/// <para>
/// The series are those the links join, except that a sound heard as an opening or an ending joins the
/// series that hears it so most often with every other series of a name that links to its own
/// (<see cref="SeriesNames.Link"/>), on whatever station, and with every series of another name whose
/// recordings could be other episodes of it in its weekly slot
/// (<see cref="SeriesLinks.AnotherEpisodeInTheSlot"/>) and hear it in two episodes or only in that half:
/// the sound links those. When every whole stretch is of that one series, the opening or ending comes
/// before the advert of two stations, so that a series whose opening fills a slot on the grid, broadcast
/// on two stations, keeps its opening. When the entry is an opening or an ending, a whole stretch in a
/// series that hears it so is taken for the kind of the half it is heard in, so that the opening sung at
/// the end of a last episode is that episode's ending, and a shorter one on the grid heard outside those
/// series is an advert there, the song played in another series' break.
/// </para>
/// </summary>
public static class CatalogKinds
{
    /// <summary>
    /// The version an entry is put back to when what it was estimated from changed under it: an answer on one
    /// of its recordings was given or withdrawn, or an entry heard in one of them began or stopped linking series.
    /// </summary>
    public const int Unsettled = 0;

    /// <summary>
    /// The version of these rows an entry was estimated with before the rows for openings and endings were
    /// applied.
    /// </summary>
    public const int FirstVersion = 1;

    /// <summary>
    /// The version of these rows: an entry estimated with an earlier one is estimated again.
    /// </summary>
    public const int Version = 2;

    public static readonly TimeSpan Grid = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan ShortOfTheGrid = TimeSpan.FromSeconds(1.5);

    public static readonly TimeSpan OverTheGrid = TimeSpan.FromSeconds(0.5);

    public static readonly TimeSpan WholeWithin = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan ThemeShortest = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan ThemeLongest = TimeSpan.FromMinutes(3);

    public static readonly TimeSpan SamePlaceWithin = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan ProgrammeFixtureShorterThan = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan EdgeWithin = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan StationFixtureShorterThan = TimeSpan.FromSeconds(15);

    private const int FewestOnTheGrid = 1;

    private const int MostOnTheGrid = 8;

    private const int MostInOneSeries = 4;

    private const int FewestEpisodes = 2;

    private const double Certain = 1.0;

    private const double AcrossSeries = 0.9;

    private const double EveryEpisode = 0.8;

    private const double SamePlace = 0.7;

    private const double AtTheEdges = 0.6;

    private const double OneSeries = 0.5;

    public static CatalogPattern Estimate(TimeSpan length, IReadOnlyList<PlacedOccurrence> occurrences, SeriesLinks links)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(links);

        PlacedOccurrence[] whole = [.. occurrences.Where(placed => IsWhole(placed.Occurrence.Length, length))];
        PlacedOccurrence[] counted = whole.Length > 0 ? whole : [.. occurrences];
        IReadOnlyList<IReadOnlyList<PlacedOccurrence>> linked = links.Group(counted, placed => placed.Occurrence.Member);
        Theme? theme = ThemeOf(length, linked);
        IReadOnlyList<IReadOnlyList<PlacedOccurrence>> series = theme?.Series ?? linked;
        int stations = counted.Select(placed => (placed.Occurrence.NetworkId, placed.Occurrence.ServiceId)).Distinct().Count();
        CatalogEstimate estimate = Answered(counted, theme) ?? Spread(length, series, stations, theme);
        IReadOnlyList<PlacedOccurrence> own = theme?.Own ?? series.MaxBy(group => group.Count) ?? [];

        return new CatalogPattern(
            estimate,
            series.Count,
            stations,
            [.. occurrences.Select(placed => Override(placed, length, estimate.Kind, own, links))]);
    }

    /// <summary>
    /// Whether a length is no more than <see cref="ShortOfTheGrid"/> short of a multiple of <see cref="Grid"/>
    /// from one to eight, 15 to 120 seconds, and no more than <see cref="OverTheGrid"/> over it. A match is cut
    /// to the sound it hears, and an advert's slot keeps about half a second of silence at either end, so what
    /// is heard of a slot falls short of the slot rather than over it.
    /// </summary>
    public static bool OnTheGrid(TimeSpan length) => OnTheGrid(length, MostOnTheGrid);

    /// <summary>
    /// Whether a stretch of an entry of <paramref name="kind"/> and <paramref name="entryLength"/> links the
    /// recording it is heard in to the others its entry is heard in (BR-CD-011): the entry is an opening, an
    /// ending or a fixed part of a programme, the stretch covers it whole, and the stretch alone is not
    /// taken for an advert.
    /// </summary>
    public static bool LinksSeries(CatalogKind kind, TimeSpan entryLength, CatalogOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        return LinksSeries(kind) && IsWhole(occurrence.Length, entryLength) && occurrence.KindOverride is not SegmentKind.Break;
    }

    /// <summary>
    /// Whether an entry of <paramref name="kind"/> links the recordings it is heard in: an opening, an ending or
    /// a fixed part of a programme.
    /// </summary>
    public static bool LinksSeries(CatalogKind kind) => kind is CatalogKind.Opening or CatalogKind.Ending or CatalogKind.ProgrammeFixture;

    private static bool OnTheGrid(TimeSpan length, int mostMultiple)
    {
        long multiple = (long)Math.Round((length + ((ShortOfTheGrid - OverTheGrid) / 2)) / Grid, MidpointRounding.AwayFromZero);
        TimeSpan slot = Grid * multiple;

        return multiple >= FewestOnTheGrid && multiple <= mostMultiple && length >= slot - ShortOfTheGrid && length <= slot + OverTheGrid;
    }

    private static bool IsWhole(TimeSpan stretch, TimeSpan length) => stretch >= length - WholeWithin;

    /// <summary>
    /// What the answers on the stretches say of the entry. An answer of a break, an opening or an ending
    /// decides outright, the one given most, a tie going to the break, then the opening. Answers of the
    /// programme alone rule out the rows that make it an advert or a fixed part of the station's output: the
    /// opening or ending it is heard as decides, and otherwise they make it a fixed part of the programme.
    /// </summary>
    private static CatalogEstimate? Answered(IEnumerable<PlacedOccurrence> counted, Theme? theme)
    {
        SegmentKind[] answers = [.. counted.Select(placed => placed.Answer).OfType<SegmentKind>()];
        SegmentKind[] deciding = [.. answers.Where(answer => answer is not SegmentKind.Programme)];

        if (deciding.Length > 0)
        {
            SegmentKind most = deciding.GroupBy(answer => answer).OrderByDescending(group => group.Count()).ThenBy(group => group.Key).First().Key;

            return new CatalogEstimate(KindOf(most), Certain, CatalogDecision.Answer);
        }

        if (answers.Length is 0)
        {
            return null;
        }

        return theme is null
            ? new CatalogEstimate(CatalogKind.ProgrammeFixture, Certain, CatalogDecision.Answer)
            : new CatalogEstimate(theme.Kind, EveryEpisode, CatalogDecision.Pattern);
    }

    private static CatalogKind KindOf(SegmentKind answer) => answer switch
    {
        SegmentKind.Break => CatalogKind.Cm,
        SegmentKind.Opening => CatalogKind.Opening,
        SegmentKind.Ending => CatalogKind.Ending,
        _ => CatalogKind.ProgrammeFixture,
    };

    private static CatalogEstimate Spread(TimeSpan length, IReadOnlyList<IReadOnlyList<PlacedOccurrence>> series, int stations, Theme? theme)
    {
        if (theme is { Prevails: true })
        {
            return new CatalogEstimate(theme.Kind, EveryEpisode, CatalogDecision.Pattern);
        }

        if (OnTheGrid(length) && (series.Count >= 2 || stations >= 2))
        {
            return new CatalogEstimate(CatalogKind.Cm, AcrossSeries, CatalogDecision.Pattern);
        }

        if (theme is not null)
        {
            return new CatalogEstimate(theme.Kind, EveryEpisode, CatalogDecision.Pattern);
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

    /// <summary>
    /// The opening or ending a sound of its length is heard as, when two episodes of one series hear it in
    /// the same half of their programmes; the series it is heard as that in, with every other series of the
    /// name of the one that hears it so most often, and every series in its slot that hears it in two episodes
    /// or only in that half; the series once those are joined into one; and whether every whole stretch is of
    /// that one series.
    /// </summary>
    private static Theme? ThemeOf(TimeSpan length, IReadOnlyList<IReadOnlyList<PlacedOccurrence>> linked)
    {
        if (length < ThemeShortest || length > ThemeLongest)
        {
            return null;
        }

        IReadOnlyList<PlacedOccurrence>[] recurring = [.. linked.Where(group => Recurs(group, CatalogKind.Opening) || Recurs(group, CatalogKind.Ending))];

        if (recurring.Length is 0)
        {
            return null;
        }

        CatalogKind kind = recurring.Sum(group => Episodes(group, CatalogKind.Opening)) >= recurring.Sum(group => Episodes(group, CatalogKind.Ending))
            ? CatalogKind.Opening
            : CatalogKind.Ending;
        IReadOnlyList<PlacedOccurrence> main = recurring.Where(group => Recurs(group, kind)).MaxBy(group => Episodes(group, kind))!;
        IReadOnlyList<PlacedOccurrence>[] joined =
        [
            .. linked.Where(group => ReferenceEquals(group, main) || NamedAs(group, main) || (InTheSlotOf(group, main) && (Recurs(group, kind) || HeardOnlyAs(group, kind)))),
        ];
        PlacedOccurrence[] slot = [.. joined.SelectMany(group => group)];
        IReadOnlyList<IReadOnlyList<PlacedOccurrence>> series =
        [
            .. linked.Where(group => !joined.Contains(group) || ReferenceEquals(group, joined[0])).Select(group => joined.Contains(group) ? slot : group),
        ];

        bool prevails = joined.Length == linked.Count;

        return new Theme(kind, [.. slot, .. recurring.Where(group => !joined.Contains(group)).SelectMany(group => group)], series, prevails);
    }

    private static bool Recurs(IReadOnlyList<PlacedOccurrence> group, CatalogKind place) => Episodes(group, place) >= FewestEpisodes;

    private static int Episodes(IReadOnlyList<PlacedOccurrence> group, CatalogKind place)
        => group.Where(placed => PlaceOf(placed) == place).Select(placed => placed.Occurrence.RecordingId).Distinct().Count();

    private static bool HeardOnlyAs(IReadOnlyList<PlacedOccurrence> group, CatalogKind place)
        => group.Any(placed => PlaceOf(placed) == place) && group.All(placed => PlaceOf(placed) is null || PlaceOf(placed) == place);

    private static bool InTheSlotOf(IReadOnlyList<PlacedOccurrence> group, IReadOnlyList<PlacedOccurrence> main)
    {
        SeriesMember[] episodes = [.. main.Select(member => member.Occurrence.Member)];

        return group.All(placed => SeriesLinks.AnotherEpisodeInTheSlot(placed.Occurrence.Member, episodes));
    }

    private static bool NamedAs(IReadOnlyList<PlacedOccurrence> group, IReadOnlyList<PlacedOccurrence> main)
        => group.Any(placed => main.Any(member => SeriesNames.Link(member.Occurrence.SeriesName, placed.Occurrence.SeriesName)));

    /// <summary>
    /// What the place of a stretch makes a theme heard there: an opening in the first half of its programme,
    /// an ending in the second, by where the middle of the stretch falls; null when the programme is not known.
    /// </summary>
    private static CatalogKind? PlaceOf(PlacedOccurrence placed)
    {
        if (placed.ProgrammeStarts is not TimeSpan starts || placed.ProgrammeEnds is not TimeSpan ends || ends <= starts)
        {
            return null;
        }

        TimeSpan middle = placed.Occurrence.StartsAt + (placed.Occurrence.Length / 2);

        return middle < starts + ((ends - starts) / 2) ? CatalogKind.Opening : CatalogKind.Ending;
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

    private static SegmentKind? Override(PlacedOccurrence placed, TimeSpan length, CatalogKind kind, IReadOnlyList<PlacedOccurrence> own, SeriesLinks links)
    {
        if (placed.Answer is SegmentKind answer)
        {
            return answer;
        }

        if (kind is not (CatalogKind.Opening or CatalogKind.Ending))
        {
            return null;
        }

        bool atHome = own.Any(member => links.Link(member.Occurrence.Member, placed.Occurrence.Member));

        return (IsWhole(placed.Occurrence.Length, length), atHome) switch
        {
            (true, true) => PlaceOf(placed) is CatalogKind place && place != kind ? CatalogHeard.CountsAs(place) : null,
            (false, false) => OnTheGrid(placed.Occurrence.Length) ? SegmentKind.Break : null,
            _ => null,
        };
    }

    private sealed record Theme(CatalogKind Kind, IReadOnlyList<PlacedOccurrence> Own, IReadOnlyList<IReadOnlyList<PlacedOccurrence>> Series, bool Prevails);
}
