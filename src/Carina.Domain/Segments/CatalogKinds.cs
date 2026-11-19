namespace Carina.Domain.Segments;

public sealed record CatalogEstimate(CatalogKind Kind, double? Confidence, CatalogDecision? DecidedBy)
{
    public static readonly CatalogEstimate Unknown = new(CatalogKind.Unknown, null, null);
}

public sealed record CatalogPattern(CatalogEstimate Estimate, int SeriesCount, int StationCount, IReadOnlyList<SegmentKind?> Overrides);

public static class CatalogKinds
{
    public const int Unsettled = 0;

    public const int FirstVersion = 1;

    public const int Version = 5;

    public static readonly TimeSpan Grid = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan ShortOfTheGrid = TimeSpan.FromSeconds(1.5);

    public static readonly TimeSpan OverTheGrid = TimeSpan.FromSeconds(0.5);

    public static readonly TimeSpan WholeWithin = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan ThemeShortest = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan ThemeLongest = TimeSpan.FromMinutes(3);

    public static readonly TimeSpan ThemeEdgeWithin = TimeSpan.FromMinutes(6);

    public static readonly TimeSpan SamePlaceWithin = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan ProgrammeFixtureShorterThan = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan EdgeWithin = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan StationFixtureShorterThan = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan PiecesWithin = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan AdvertsWithin = TimeSpan.FromSeconds(2);

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

        PlacedOccurrence[] counted = Counted(length, occurrences);
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

    public static CatalogPattern Estimate(TimeSpan length, IReadOnlyList<PlacedOccurrence> occurrences, SeriesLinks links, CatalogSurroundings around)
    {
        ArgumentNullException.ThrowIfNull(around);

        CatalogPattern own = Estimate(length, occurrences, links);

        if (!MaySettle(own))
        {
            return own;
        }

        CatalogEntryId[] itself = [.. occurrences.Select(placed => placed.Occurrence.EntryId).Distinct()];

        return Settle(length, occurrences, own, around.Replacing(itself, Stretches(occurrences, own)));
    }

    public static CatalogPattern Settle(TimeSpan length, IReadOnlyList<PlacedOccurrence> occurrences, CatalogPattern own, CatalogSurroundings around)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(around);

        return CarryOn(length, occurrences, Sponsored(length, occurrences, own, around), around);
    }

    public static IReadOnlyDictionary<CatalogEntryId, CatalogPattern> SettleTogether(
        IReadOnlyList<CatalogSound> sounds,
        IReadOnlyDictionary<CatalogEntryId, CatalogPattern> own,
        CatalogSurroundings around)
    {
        ArgumentNullException.ThrowIfNull(sounds);
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(around);

        Dictionary<CatalogEntryId, CatalogPattern> settled = sounds.ToDictionary(sound => sound.Id, sound => own[sound.Id]);
        CatalogEntryId[] settling = [.. settled.Keys];

        for (int pass = 0; pass <= sounds.Count; pass++)
        {
            CatalogSurroundings beside = around.Replacing(settling, sounds.SelectMany(sound => Stretches(sound.Occurrences, settled[sound.Id])));
            CatalogPattern[] next = [.. sounds.Select(sound => Settle(sound.Length, sound.Occurrences, own[sound.Id], beside))];

            if (sounds.Select((sound, at) => Same(next[at], settled[sound.Id])).All(same => same))
            {
                break;
            }

            for (int at = 0; at < sounds.Count; at++)
            {
                settled[sounds[at].Id] = next[at];
            }
        }

        return settled;
    }

    public static CatalogPattern CarryOn(TimeSpan length, IReadOnlyList<PlacedOccurrence> occurrences, CatalogPattern own, CatalogSurroundings around)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(around);

        if (!CarriesOn(own.Estimate, own.SeriesCount))
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

    public static bool MaySettle(CatalogPattern own)
    {
        ArgumentNullException.ThrowIfNull(own);

        return CarriesOn(own.Estimate, own.SeriesCount);
    }

    public static IEnumerable<ThemeStretch> Stretches(IReadOnlyList<PlacedOccurrence> occurrences, CatalogPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(pattern);

        return occurrences.Select((placed, at) => ThemeStretch.Of(
            placed.Occurrence.EntryId,
            placed.Occurrence.RecordingId,
            placed.Occurrence.Span,
            pattern.Estimate,
            pattern.SeriesCount,
            pattern.Overrides[at]));
    }

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

    public static bool OnTheGrid(TimeSpan length) => OnTheGrid(length, MostOnTheGrid);

    public static bool LinksSeries(CatalogKind kind, TimeSpan entryLength, CatalogOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        return LinksSeries(kind) && IsWhole(occurrence.Length, entryLength) && occurrence.KindOverride is not SegmentKind.Break;
    }

    public static bool LinksSeries(CatalogKind kind) => kind is CatalogKind.Opening or CatalogKind.Ending or CatalogKind.ProgrammeFixture;

    private static bool OnTheGrid(TimeSpan length, int mostMultiple)
    {
        long multiple = (long)Math.Round((length + ((ShortOfTheGrid - OverTheGrid) / 2)) / Grid, MidpointRounding.AwayFromZero);
        TimeSpan slot = Grid * multiple;

        return multiple >= FewestOnTheGrid && multiple <= mostMultiple && length >= slot - ShortOfTheGrid && length <= slot + OverTheGrid;
    }

    private static bool IsWhole(TimeSpan stretch, TimeSpan length) => stretch >= length - WholeWithin;

    private static PlacedOccurrence[] Counted(TimeSpan length, IReadOnlyList<PlacedOccurrence> occurrences)
    {
        PlacedOccurrence[] whole = [.. occurrences.Where(placed => IsWhole(placed.Occurrence.Length, length))];

        return whole.Length > 0 ? whole : [.. occurrences];
    }

    private static CatalogPattern Sponsored(TimeSpan length, IReadOnlyList<PlacedOccurrence> occurrences, CatalogPattern own, CatalogSurroundings around)
    {
        if (own.Estimate != new CatalogEstimate(CatalogKind.ProgrammeFixture, SamePlace, CatalogDecision.Pattern)
            || !OnTheGrid(length, MostInOneSeries)
            || !Counted(length, occurrences).Any(placed => BesideAnAdvert(placed.Occurrence, around)))
        {
            return own;
        }

        return own with { Estimate = new CatalogEstimate(CatalogKind.Cm, OneSeries, CatalogDecision.Pattern) };
    }

    private static bool BesideAnAdvert(CatalogOccurrence occurrence, CatalogSurroundings around)
        => around.In(occurrence.RecordingId).Any(stretch =>
            !stretch.Entry.Equals(occurrence.EntryId) && stretch.CountsAs is SegmentKind.Break && Apart(occurrence.Span, stretch.Span) <= AdvertsWithin);

    private static bool Same(CatalogPattern one, CatalogPattern other)
        => one.Estimate == other.Estimate
           && one.SeriesCount == other.SeriesCount
           && one.StationCount == other.StationCount
           && one.Overrides.SequenceEqual(other.Overrides);

    private static TimeSpan Apart(SoundSpan one, SoundSpan other)
        => (one.Starts > other.Starts ? one.Starts : other.Starts) - (one.Ends < other.Ends ? one.Ends : other.Ends);

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

    private sealed record Carried(PlacedOccurrence Placed, CatalogEntryId Theme, CatalogKind Kind, TimeSpan After)
    {
        public bool SameAs(Carried other)
            => Theme.Equals(other.Theme) && Kind == other.Kind && (After - other.After).Duration() <= PiecesWithin;
    }
}
