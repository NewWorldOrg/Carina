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
/// The kind of a catalog entry, from the way its stretches are spread, the first row that holds: a
/// length on the 15-second grid (<see cref="OnTheGrid"/>, a second and a half short of a multiple to half
/// a second over) heard in two series or on two stations is an advert, 0.9; a piece of
/// <see cref="ThemeShortest"/> to <see cref="ThemeLongest"/>, other than one on the grid of up to a minute,
/// heard in two episodes of one series starting within <see cref="ThemeEdgeWithin"/> after the guide's start
/// of the programme is its opening, 0.8, and ending within as much before the guide's end its ending, 0.8,
/// whichever more of those episodes hear it at, the opening on a tie, a stretch near both taken by the half
/// of the programme it is heard in; a piece shorter than 30 seconds heard at about
/// the same place (<see cref="SamePlaceWithin"/>) from the start of two episodes of one series is a fixed
/// part of the programme, 0.7; a piece shorter than 15 seconds heard within 15 seconds of the start or end
/// of programmes of two series is a fixed part of the station's output, 0.6; a length on the grid of up to
/// a minute heard in one series is an advert, 0.5, since an opening or ending is often a whole slot of a
/// minute and a half; anything else is not known. Only the stretches that cover the entry whole
/// (<see cref="WholeWithin"/>) count; a shorter one is taken on its own. Two recordings on one station of a
/// programme the guide starts at the same time are one episode (<see cref="SeriesMember.Episode"/>), so that a
/// broadcast recorded twice does not make a sound heard once in it a sound heard in two episodes.
/// <para>
/// The series are those the links join, except that a sound heard as an opening or an ending joins the
/// series that hears it so most often with every other series of the very same name
/// (<see cref="SeriesNames.Same"/>), on whatever station and at whatever time, and with every series of another
/// name whose
/// recordings could be other episodes of it in its weekly slot
/// (<see cref="SeriesLinks.AnotherEpisodeInTheSlot"/>) and hear it in two episodes or only at that place:
/// the sound links those. When every whole stretch is of that one series, the opening or ending comes
/// before the advert of two stations, so that a series whose opening fills a slot on the grid, broadcast
/// on two stations, keeps its opening. When the entry is an opening or an ending, a whole stretch in a
/// series that hears it so is taken for the kind of the place it is heard at, so that the opening sung at
/// the end of a last episode is that episode's ending and one sung in the middle of an episode, an insert
/// song, is the programme there, and a shorter one on the grid heard outside those series is an advert
/// there, the song played in another series' break.
/// </para>
/// </summary>
public static class CatalogKinds
{
    /// <summary>
    /// The version an entry is put back to when what it was estimated from changed under it: an entry heard in one
    /// of its recordings began or stopped linking series.
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
    public const int Version = 4;

    public static readonly TimeSpan Grid = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan ShortOfTheGrid = TimeSpan.FromSeconds(1.5);

    public static readonly TimeSpan OverTheGrid = TimeSpan.FromSeconds(0.5);

    public static readonly TimeSpan WholeWithin = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan ThemeShortest = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan ThemeLongest = TimeSpan.FromMinutes(3);

    /// <summary>
    /// How far after the guide's start an opening starts, and before the guide's end an ending ends, at the
    /// most.
    /// </summary>
    public static readonly TimeSpan ThemeEdgeWithin = TimeSpan.FromMinutes(6);

    public static readonly TimeSpan SamePlaceWithin = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan ProgrammeFixtureShorterThan = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan EdgeWithin = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan StationFixtureShorterThan = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How far apart two pieces of an opening or ending lie at the most, and how far from where a piece lies
    /// after the start of the opening or ending in another episode.
    /// </summary>
    public static readonly TimeSpan PiecesWithin = TimeSpan.FromSeconds(2);

    private const int FewestOnTheGrid = 1;

    private const int MostOnTheGrid = 8;

    private const int MostInOneSeries = 4;

    private const int FewestEpisodes = 2;

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
        IReadOnlyList<IReadOnlyList<PlacedOccurrence>> linked = links.Group(counted, placed => placed.Member);
        Theme? theme = ThemeOf(length, linked);
        IReadOnlyList<IReadOnlyList<PlacedOccurrence>> series = theme?.Series ?? linked;
        int stations = counted.Select(placed => (placed.Occurrence.NetworkId, placed.Occurrence.ServiceId)).Distinct().Count();
        CatalogEstimate estimate = Spread(length, series, stations, theme);
        IReadOnlyList<PlacedOccurrence> own = theme?.Own ?? series.MaxBy(group => group.Count) ?? [];

        return new CatalogPattern(
            estimate,
            series.Count,
            stations,
            [.. occurrences.Select(placed => Override(placed, length, estimate.Kind, own, links))]);
    }

    /// <summary>
    /// The pattern its own stretches give an entry of <paramref name="length"/>, carried on as an opening or an
    /// ending when the entry is a piece of one (BR-CD-005). An entry that may carry one on
    /// (<see cref="MayCarryOn"/>), whose whole stretches in two episodes lie at the same distance, give or take
    /// <see cref="PiecesWithin"/>, from the start of a stretch of one opening or ending, in the run of pieces that
    /// follows or leads up to that stretch, is that opening or ending, 0.8. In a run each piece lies no more than
    /// <see cref="PiecesWithin"/> from the next, the whole is no longer than <see cref="ThemeLongest"/>, and the
    /// pieces are the stretches of entries that may carry one on or already do. The stretches so placed are taken
    /// for the opening or ending, and every other one for what the entry is on its own, the programme when that is
    /// not known. An opening or ending is often matched in pieces, as a phrase it sings twice matches apart from the
    /// rest, and a piece shorter than <see cref="ThemeShortest"/> or on the grid is otherwise taken for something else.
    /// </summary>
    public static CatalogPattern Estimate(TimeSpan length, IReadOnlyList<PlacedOccurrence> occurrences, SeriesLinks links, CatalogSurroundings around)
    {
        ArgumentNullException.ThrowIfNull(around);

        CatalogPattern own = Estimate(length, occurrences, links);

        if (!MayCarryOn(own))
        {
            return own;
        }

        CatalogEntryId[] itself = [.. occurrences.Select(placed => placed.Occurrence.EntryId).Distinct()];

        return CarryOn(length, occurrences, own, around.Replacing(itself, Stretches(occurrences, own)));
    }

    /// <summary>
    /// <paramref name="own"/>, what the stretches of an entry give it on their own, carried on as an opening or
    /// an ending when the entry is a piece of one heard in <paramref name="around"/>, which holds the entry's
    /// own stretches as <paramref name="own"/> takes them (<see cref="Estimate(TimeSpan, IReadOnlyList{PlacedOccurrence}, SeriesLinks, CatalogSurroundings)"/>).
    /// </summary>
    public static CatalogPattern CarryOn(TimeSpan length, IReadOnlyList<PlacedOccurrence> occurrences, CatalogPattern own, CatalogSurroundings around)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(around);

        if (!MayCarryOn(own))
        {
            return own;
        }

        Carried[] carried = [.. occurrences.Where(placed => IsWhole(placed.Occurrence.Length, length)).SelectMany(placed => Carriers(placed, around))];
        Carried[]? kept = carried
            .Select(one => (Carried[])[.. carried.Where(other => other.SameAs(one))])
            .Where(together => Episodes(together) >= FewestEpisodes)
            .OrderByDescending(Episodes)
            .ThenBy(together => together[0].Kind is CatalogKind.Opening ? 0 : 1)
            .FirstOrDefault();

        if (kept is null)
        {
            return own;
        }

        HashSet<CatalogOccurrence> taken = [.. kept.Select(one => one.Placed.Occurrence)];
        SegmentKind elsewhere = CatalogHeard.CountsAs(own.Estimate.Kind) ?? SegmentKind.Programme;

        return new CatalogPattern(
            new CatalogEstimate(kept[0].Kind, EveryEpisode, CatalogDecision.Continuation),
            own.SeriesCount,
            own.StationCount,
            [.. occurrences.Select(placed => taken.Contains(placed.Occurrence) ? null : (SegmentKind?)elsewhere)]);
    }

    /// <summary>
    /// Whether an entry its own stretches take as <paramref name="own"/> may carry on an opening or an ending: it is of
    /// one series, and taken for a programme fixture, for an advert of one series or for nothing known.
    /// </summary>
    public static bool MayCarryOn(CatalogPattern own)
    {
        ArgumentNullException.ThrowIfNull(own);

        return CarriesOn(own.Estimate, own.SeriesCount);
    }

    /// <summary>
    /// The stretches of an entry taken as <paramref name="pattern"/>, each with what it is to an opening or an ending
    /// (<see cref="PartOf"/>).
    /// </summary>
    public static IEnumerable<ThemeStretch> Stretches(IReadOnlyList<PlacedOccurrence> occurrences, CatalogPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(pattern);

        return occurrences.Select((placed, at) => new ThemeStretch(
            placed.Occurrence.EntryId,
            placed.Occurrence.RecordingId,
            placed.Occurrence.Span,
            PartOf(pattern.Estimate, pattern.SeriesCount, pattern.Overrides[at])));
    }

    /// <summary>
    /// What a stretch of an entry taken for <paramref name="estimate"/>, in <paramref name="seriesCount"/> series, is to an
    /// opening or an ending, when it alone is taken for <paramref name="kindOverride"/>: a piece, when the entry
    /// carries one on or could; the opening or the ending, when the entry is one by its own stretches and the stretch counts as
    /// one; nothing otherwise.
    /// </summary>
    public static ThemePart PartOf(CatalogEstimate estimate, int seriesCount, SegmentKind? kindOverride)
    {
        ArgumentNullException.ThrowIfNull(estimate);

        if (estimate.DecidedBy is CatalogDecision.Continuation || CarriesOn(estimate, seriesCount))
        {
            return ThemePart.Piece;
        }

        if (estimate.Kind is not (CatalogKind.Opening or CatalogKind.Ending))
        {
            return ThemePart.None;
        }

        return (kindOverride ?? CatalogHeard.CountsAs(estimate.Kind)) switch
        {
            SegmentKind.Opening => ThemePart.Opening,
            SegmentKind.Ending => ThemePart.Ending,
            _ => ThemePart.None,
        };
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
    /// The opening or ending a sound of its length is heard as, when two episodes of one series hear it at
    /// that place in their programmes; the series it is heard as that in, with every other series of the very
    /// same name as the one that hears it so most often, and every series in its slot that hears it in two episodes
    /// or only at that place; the series once those are joined into one; and whether every whole stretch is of
    /// that one series.
    /// </summary>
    private static Theme? ThemeOf(TimeSpan length, IReadOnlyList<IReadOnlyList<PlacedOccurrence>> linked)
    {
        if (length < ThemeShortest || length > ThemeLongest || OnTheGrid(length, MostInOneSeries))
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
        => group.Where(placed => PlaceOf(placed) == place).Select(placed => placed.Episode).Distinct().Count();

    private static bool HeardOnlyAs(IReadOnlyList<PlacedOccurrence> group, CatalogKind place)
        => group.Any(placed => PlaceOf(placed) == place) && group.All(placed => PlaceOf(placed) is null || PlaceOf(placed) == place);

    private static bool InTheSlotOf(IReadOnlyList<PlacedOccurrence> group, IReadOnlyList<PlacedOccurrence> main)
    {
        SeriesMember[] episodes = [.. main.Select(member => member.Member)];

        return group.All(placed => SeriesLinks.AnotherEpisodeInTheSlot(placed.Member, episodes));
    }

    private static bool NamedAs(IReadOnlyList<PlacedOccurrence> group, IReadOnlyList<PlacedOccurrence> main)
        => group.Any(placed => main.Any(member => SeriesNames.Same(member.Occurrence.Member.SeriesName, placed.Occurrence.Member.SeriesName)));

    /// <summary>
    /// What the place of a stretch makes a theme heard there, by the guide's start and end of its programme:
    /// an opening when it starts no more than <see cref="EdgeWithin"/> before the start or
    /// <see cref="ThemeEdgeWithin"/> after it, an ending when it ends no more than as much before the end or
    /// <see cref="EdgeWithin"/> after it, by the half its middle falls in when it is both, and neither,
    /// <see cref="CatalogKind.Unknown"/>, anywhere else; null when the programme is not known.
    /// </summary>
    private static CatalogKind? PlaceOf(PlacedOccurrence placed)
    {
        if (placed.ProgrammeStarts is not TimeSpan starts || placed.ProgrammeEnds is not TimeSpan ends || ends <= starts)
        {
            return null;
        }

        CatalogOccurrence occurrence = placed.Occurrence;
        bool opens = occurrence.StartsAt >= starts - EdgeWithin && occurrence.StartsAt <= starts + ThemeEdgeWithin;
        bool closes = occurrence.EndsAt >= ends - ThemeEdgeWithin && occurrence.EndsAt <= ends + EdgeWithin;

        return (opens, closes) switch
        {
            (true, true) => occurrence.StartsAt + (occurrence.Length / 2) < starts + ((ends - starts) / 2) ? CatalogKind.Opening : CatalogKind.Ending,
            (true, false) => CatalogKind.Opening,
            (false, true) => CatalogKind.Ending,
            _ => CatalogKind.Unknown,
        };
    }

    private static bool HeardTwiceInOnePlace(IReadOnlyList<PlacedOccurrence> series)
        => series.Any(one => series.Any(other => SamePlaceInAnotherEpisode(one, other)));

    private static bool SamePlaceInAnotherEpisode(PlacedOccurrence one, PlacedOccurrence other)
        => !one.Episode.Equals(other.Episode)
           && one.IntoProgramme is TimeSpan here
           && other.IntoProgramme is TimeSpan there
           && (here - there).Duration() <= SamePlaceWithin;

    private static bool AtAnEdge(PlacedOccurrence placed)
        => Near(placed.Occurrence, placed.ProgrammeStarts) || Near(placed.Occurrence, placed.ProgrammeEnds);

    private static bool Near(CatalogOccurrence occurrence, TimeSpan? edge)
        => edge is TimeSpan at && occurrence.StartsAt >= at - EdgeWithin && occurrence.EndsAt <= at + EdgeWithin;

    private static SegmentKind? Override(PlacedOccurrence placed, TimeSpan length, CatalogKind kind, IReadOnlyList<PlacedOccurrence> own, SeriesLinks links)
    {
        if (kind is not (CatalogKind.Opening or CatalogKind.Ending))
        {
            return null;
        }

        bool atHome = own.Any(member => links.Link(member.Member, placed.Member));

        return (IsWhole(placed.Occurrence.Length, length), atHome) switch
        {
            (true, true) => ByPlace(placed, kind),
            (false, false) => OnTheGrid(placed.Occurrence.Length) ? SegmentKind.Break : null,
            _ => null,
        };
    }

    private static SegmentKind? ByPlace(PlacedOccurrence placed, CatalogKind kind) => PlaceOf(placed) switch
    {
        CatalogKind.Unknown => SegmentKind.Programme,
        CatalogKind place when place != kind => CatalogHeard.CountsAs(place),
        _ => null,
    };

    private static bool CarriesOn(CatalogEstimate estimate, int seriesCount)
        => seriesCount == 1
           && (estimate.Kind is CatalogKind.ProgrammeFixture or CatalogKind.Unknown || estimate == new CatalogEstimate(CatalogKind.Cm, OneSeries, CatalogDecision.Pattern));

    private static int Episodes(Carried[] together) => together.Select(one => one.Placed.Episode).Distinct().Count();

    /// <summary>
    /// The openings and endings whose run of pieces in its recording holds <paramref name="placed"/>, each with how far
    /// after the start of its stretch there <paramref name="placed"/> starts.
    /// </summary>
    private static IEnumerable<Carried> Carriers(PlacedOccurrence placed, CatalogSurroundings around)
    {
        IReadOnlyList<ThemeStretch> heard = around.In(placed.Occurrence.RecordingId);
        ThemeStretch? self = heard.FirstOrDefault(stretch => stretch.Entry.Equals(placed.Occurrence.EntryId) && stretch.Span.Equals(placed.Occurrence.Span));

        if (self is not { Part: ThemePart.Piece })
        {
            return [];
        }

        return heard
            .Where(stretch => stretch.Part is ThemePart.Opening or ThemePart.Ending)
            .Where(theme => Run(heard, theme).Contains(self))
            .Select(theme => new Carried(placed, theme.Entry, theme.Part is ThemePart.Opening ? CatalogKind.Opening : CatalogKind.Ending, placed.Occurrence.StartsAt - theme.Span.Starts));
    }

    /// <summary>
    /// The stretch of an opening or ending and the pieces that follow it, then those that lead up to it, each no more
    /// than <see cref="PiecesWithin"/> from the run so far, so long as the run is no longer than <see cref="ThemeLongest"/>:
    /// pieces, and stretches of the same part. A stretch the run already covers is passed over, and any other one
    /// ends the run on its side.
    /// </summary>
    private static List<ThemeStretch> Run(IReadOnlyList<ThemeStretch> heard, ThemeStretch theme)
    {
        List<ThemeStretch> run = [theme];
        TimeSpan starts = theme.Span.Starts;
        TimeSpan ends = theme.Span.Ends;

        foreach (ThemeStretch next in heard)
        {
            if (next.Span.Ends <= ends)
            {
                continue;
            }

            if (next.Span.Starts > ends + PiecesWithin || !Joins(next, theme) || next.Span.Ends - starts > ThemeLongest)
            {
                break;
            }

            run.Add(next);
            ends = next.Span.Ends;
        }

        foreach (ThemeStretch before in heard.OrderByDescending(stretch => stretch.Span.Ends))
        {
            if (before.Span.Starts >= starts)
            {
                continue;
            }

            if (before.Span.Ends < starts - PiecesWithin || !Joins(before, theme) || ends - before.Span.Starts > ThemeLongest)
            {
                break;
            }

            run.Add(before);
            starts = before.Span.Starts;
        }

        return run;
    }

    private static bool Joins(ThemeStretch stretch, ThemeStretch theme) => stretch.Part is ThemePart.Piece || stretch.Part == theme.Part;

    private sealed record Theme(CatalogKind Kind, IReadOnlyList<PlacedOccurrence> Own, IReadOnlyList<IReadOnlyList<PlacedOccurrence>> Series, bool Prevails);

    /// <summary>
    /// A whole stretch of an entry in the run of pieces of the opening or ending of <paramref name="Theme"/>, of
    /// <paramref name="Kind"/>, starting <paramref name="After"/> the start of its stretch there.
    /// </summary>
    private sealed record Carried(PlacedOccurrence Placed, CatalogEntryId Theme, CatalogKind Kind, TimeSpan After)
    {
        public bool SameAs(Carried other)
            => Theme.Equals(other.Theme) && Kind == other.Kind && (After - other.After).Duration() <= PiecesWithin;
    }
}
