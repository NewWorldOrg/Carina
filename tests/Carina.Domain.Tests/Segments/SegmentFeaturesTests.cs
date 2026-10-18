using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentFeaturesTests
{
    private const double Length = 1800;

    private static readonly DateTime Evening = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    [Theory(DisplayName = "a piece is measured by its length, how far it lies off the 15-second grid, and whether it is a multiple of 15 seconds from 15 seconds to 3 minutes")]
    [InlineData(60.7, 0.7, 1)]
    [InlineData(60.9, 0.9, 1)]
    [InlineData(61.1, 1.1, 0)]
    [InlineData(62.5, 2.5, 0)]
    [InlineData(20, 5, 0)]
    [InlineData(14.5, 0.5, 0)]
    [InlineData(180, 0, 1)]
    [InlineData(195, 0, 0)]
    public void APieceIsMeasuredAgainstTheGrid(double seconds, double offTheGrid, double multiple)
    {
        SegmentFeatureValues features = Pieces([Edge(0), Cut(300), Cut(300 + seconds), Edge(Length)])[1].Features;

        Assert.Equal(seconds, features[SegmentFeature.Length]!.Value, 9);
        Assert.Equal(offTheGrid, features[SegmentFeature.GridOffset]!.Value, 9);
        Assert.Equal(multiple, features[SegmentFeature.IsMultipleOf15s]);
    }

    [Fact(DisplayName = "a piece is fitted to the grid from any of the cuts its edges took in")]
    public void APieceIsFittedFromAnyCutItsEdgesTookIn()
    {
        IReadOnlyList<SegmentPiece> pieces = Pieces([Edge(0), Cut(300), Cut(360, latest: 361.5), Cut(421.5), Edge(Length)]);

        Assert.Equal(TimeSpan.FromSeconds(60), pieces[1].Fitted);
        Assert.Equal(TimeSpan.FromSeconds(60), pieces[2].Fitted);
        Assert.Equal(1, pieces[1].Features[SegmentFeature.IsMultipleOf15s]);
        Assert.Equal(1, pieces[2].Features[SegmentFeature.IsMultipleOf15s]);
        Assert.Equal(61.5, pieces[2].Features[SegmentFeature.Length]!.Value, 9);
        Assert.Equal(0, pieces[2].Features[SegmentFeature.GridOffset]!.Value, 9);
    }

    [Fact(DisplayName = "the silence at an edge is a silent cut at both edges or nothing, and its depth and length are read at each edge")]
    public void EdgeSilenceNeedsACutAtBothEdges()
    {
        IReadOnlyList<SegmentPiece> pieces = Pieces([Edge(0), Cut(300, depth: 12, lasting: 0.4), Cut(360, depth: 30, lasting: 0.6), Plain(420), Edge(Length)]);

        Assert.Equal(1, pieces[1].Features[SegmentFeature.EdgeSilence]);
        Assert.Equal(0, pieces[2].Features[SegmentFeature.EdgeSilence]);
        Assert.Equal(0, pieces[0].Features[SegmentFeature.EdgeSilence]);
        Assert.Equal(12, pieces[1].Features[SegmentFeature.HeadSilenceDepth]);
        Assert.Equal(0.4, pieces[1].Features[SegmentFeature.HeadSilenceLength]!.Value, 9);
        Assert.Equal(30, pieces[1].Features[SegmentFeature.TailSilenceDepth]);
        Assert.Equal(0.6, pieces[1].Features[SegmentFeature.TailSilenceLength]!.Value, 9);
        Assert.Equal(0, pieces[2].Features[SegmentFeature.TailSilenceDepth]);
    }

    [Fact(DisplayName = "the logo share counts the seconds inside a piece away from its edges, and is not known with fewer than three of them or no logo to tell by")]
    public void TheLogoShareIsReadInsideThePiece()
    {
        bool[] seen = [.. Enumerable.Range(0, (int)Length).Select(second => second is >= 300 and < 330)];
        BoundaryCandidate[] candidates = [Edge(0), Cut(300), Cut(360), Cut(1000), Cut(1006), Edge(Length)];

        IReadOnlyList<SegmentPiece> pieces = SegmentFeatures.Of(candidates, new SyntheticReadings(Length).Evidence(), Context(), seen, SegmentThresholds.Initial);
        IReadOnlyList<SegmentPiece> blind = SegmentFeatures.Of(candidates, new SyntheticReadings(Length).Evidence(), Context(), null, SegmentThresholds.Initial);

        Assert.Equal(27d / 55d, pieces[1].Features[SegmentFeature.LogoShare]!.Value, 9);
        Assert.Equal(0, pieces[2].Features[SegmentFeature.LogoShare]);
        Assert.Null(pieces[3].Features[SegmentFeature.LogoShare]);
        Assert.Null(blind[1].Features[SegmentFeature.LogoShare]);
    }

    [Fact(DisplayName = "the catalog shares count each stretch as the kind it counts as, a programme fixture as programme and a station fixture as a break")]
    public void TheCatalogSharesCountEachStretchByItsKind()
    {
        SegmentContext context = Context(catalog:
        [
            new CatalogHeard(TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(330), SegmentKind.Break, 5, 3, 2),
            new CatalogHeard(TimeSpan.FromSeconds(320), TimeSpan.FromSeconds(345), SegmentKind.Break, 2, 1, 1),
            new CatalogHeard(TimeSpan.FromSeconds(345), TimeSpan.FromSeconds(351), SegmentKind.Programme, 4, 1, 1),
            new CatalogHeard(TimeSpan.FromSeconds(351), TimeSpan.FromSeconds(354), null, 2, 1, 1),
        ]);

        SegmentFeatureValues features = SegmentFeatures.Of(
            [Edge(0), Cut(300), Cut(360), Edge(Length)],
            new SyntheticReadings(Length).Evidence(),
            context,
            null,
            SegmentThresholds.Initial)[1].Features;

        Assert.Equal(45d / 60d, features[SegmentFeature.CatalogCmShare]!.Value, 9);
        Assert.Equal(6d / 60d, features[SegmentFeature.CatalogProgrammeShare]!.Value, 9);
        Assert.Equal(54d / 60d, features[SegmentFeature.CatalogShare]!.Value, 9);
        Assert.Equal(0, features[SegmentFeature.CatalogOpeningShare]);
        Assert.Equal(5, features[SegmentFeature.CatalogOccurrences]);
        Assert.Equal(3, features[SegmentFeature.CatalogSeries]);
        Assert.Equal(2, features[SegmentFeature.CatalogStations]);
    }

    [Theory(DisplayName = "a catalog entry counts as the kind of segment it stands for")]
    [InlineData(CatalogKind.Cm, SegmentKind.Break)]
    [InlineData(CatalogKind.StationFixture, SegmentKind.Break)]
    [InlineData(CatalogKind.ProgrammeFixture, SegmentKind.Programme)]
    [InlineData(CatalogKind.Opening, SegmentKind.Opening)]
    [InlineData(CatalogKind.Ending, SegmentKind.Ending)]
    [InlineData(CatalogKind.Unknown, null)]
    public void ACatalogEntryCountsAsItsKind(CatalogKind kind, SegmentKind? counts)
    {
        CatalogEntry entry = Entry(kind);

        Assert.Equal(counts, CatalogHeard.Of(Occurrence(entry, null), entry).Kind);
    }

    [Fact(DisplayName = "what one stretch alone is taken for wins over its entry's kind")]
    public void AStretchsOwnKindWins()
    {
        CatalogEntry opening = Entry(CatalogKind.Opening);

        Assert.Equal(SegmentKind.Break, CatalogHeard.Of(Occurrence(opening, SegmentKind.Break), opening).Kind);
        Assert.Throws<ArgumentException>(() => CatalogHeard.Of(Occurrence(opening, null), Entry(CatalogKind.Opening)));
    }

    [Fact(DisplayName = "a piece is placed from the programme's start and end by the guide, and the share of it outside the guide is read")]
    public void APieceIsPlacedByTheGuide()
    {
        SegmentContext context = Context(starts: TimeSpan.FromSeconds(60), ends: TimeSpan.FromSeconds(1740));

        IReadOnlyList<SegmentPiece> pieces = SegmentFeatures.Of(
            [Edge(0), Cut(30), Cut(900), Cut(1770), Edge(Length)],
            new SyntheticReadings(Length).Evidence(),
            context,
            null,
            SegmentThresholds.Initial);
        IReadOnlyList<SegmentPiece> unguided = SegmentFeatures.Of([Edge(0), Cut(900), Edge(Length)], new SyntheticReadings(Length).Evidence(), Context(), null, SegmentThresholds.Initial);

        Assert.Equal(-30d / 1680, pieces[1].Features[SegmentFeature.FromStart]!.Value, 9);
        Assert.Equal(840d / 1680, pieces[1].Features[SegmentFeature.FromEnd]!.Value, 9);
        Assert.Equal(30d / 870, pieces[1].Features[SegmentFeature.OutsideGuide]!.Value, 9);
        Assert.Equal(30d / 870, pieces[2].Features[SegmentFeature.OutsideGuide]!.Value, 9);
        Assert.Equal(1, pieces[0].Features[SegmentFeature.OutsideGuide]);
        Assert.Equal(0.5, unguided[1].Features[SegmentFeature.FromStart]!.Value, 9);
        Assert.Null(unguided[1].Features[SegmentFeature.OutsideGuide]);
    }

    [Fact(DisplayName = "the past break share is how many earlier episodes had a break within 30 seconds of the same place from their start")]
    public void ThePastBreakShareComesFromEarlierEpisodes()
    {
        PastEpisode[] past =
        [
            Episode(10, (310, 370)),
            Episode(20, (360, 400)),
            Episode(0, (900, 960)),
            Episode(0),
        ];

        IReadOnlyList<SegmentPiece> pieces = SegmentFeatures.Of(
            [Edge(0), Cut(300), Cut(360), Edge(Length)],
            new SyntheticReadings(Length).Evidence(),
            Context(starts: TimeSpan.Zero, past: past),
            null,
            SegmentThresholds.Initial);

        Assert.Equal(2d / 4, pieces[1].Features[SegmentFeature.PastCmShare]!.Value, 9);
        Assert.Null(Pieces([Edge(0), Cut(300), Cut(360), Edge(Length)])[1].Features[SegmentFeature.PastCmShare]);
    }

    [Fact(DisplayName = "the caption share and the channels' mean likeness are read over the piece, and are not known without readings")]
    public void CaptionsAndChannelsAreReadOverThePiece()
    {
        SegmentEvidence evidence = new SyntheticReadings(Length).Captioned(second => second is >= 300 and < 315).Channels(330, 360, 100).Evidence();

        SegmentFeatureValues features = SegmentFeatures.Of([Edge(0), Cut(300), Cut(360), Edge(Length)], evidence, Context(), null, SegmentThresholds.Initial)[1].Features;
        SegmentFeatureValues uncaptioned = Pieces([Edge(0), Cut(300), Cut(360), Edge(Length)])[1].Features;

        Assert.Equal(0.25, features[SegmentFeature.CaptionShare]!.Value, 9);
        Assert.Equal(0.45, features[SegmentFeature.ChannelCorrelation]!.Value, 9);
        Assert.Null(uncaptioned[SegmentFeature.CaptionShare]);
    }

    [Fact(DisplayName = "the programme's first genre sets its own feature and clears the others, and no genre leaves them all unknown")]
    public void TheFirstGenreIsSet()
    {
        SegmentFeatureValues anime = SegmentFeatures.Of(
            [Edge(0), Edge(Length)],
            new SyntheticReadings(Length).Evidence(),
            Context(genres: [new ProgrammeGenre(7, 0), new ProgrammeGenre(3, 1)]),
            null,
            SegmentThresholds.Initial)[0].Features;
        SegmentFeatureValues none = Pieces([Edge(0), Edge(Length)])[0].Features;

        Assert.Equal(1, anime[SegmentFeature.Genre(7)]);
        Assert.Equal(0, anime[SegmentFeature.Genre(3)]);
        Assert.Equal(0, anime[SegmentFeature.Genre(0)]);
        Assert.Null(none[SegmentFeature.Genre(7)]);
    }

    [Fact(DisplayName = "the two interactions are a multiple of 15 seconds without the logo, and a catalog advert on a multiple of 15 seconds")]
    public void TheInteractionsAreProducts()
    {
        bool[] seen = [.. Enumerable.Range(0, (int)Length).Select(second => second is >= 300 and < 312)];
        SegmentContext context = Context(catalog: [new CatalogHeard(TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(330), SegmentKind.Break, 2, 2, 1)]);

        SegmentFeatureValues features = SegmentFeatures.Of([Edge(0), Cut(300), Cut(360), Edge(Length)], new SyntheticReadings(Length).Evidence(), context, seen, SegmentThresholds.Initial)[1].Features;
        SegmentFeatureValues blind = SegmentFeatures.Of([Edge(0), Cut(300), Cut(362), Edge(Length)], new SyntheticReadings(Length).Evidence(), context, null, SegmentThresholds.Initial)[1].Features;

        Assert.Equal(1 - (9d / 55), features[SegmentFeature.MultipleOf15sWithoutLogo]!.Value, 9);
        Assert.Equal(0.5, features[SegmentFeature.CatalogCmMultipleOf15s]!.Value, 9);
        Assert.Null(blind[SegmentFeature.MultipleOf15sWithoutLogo]);
        Assert.Equal(0, blind[SegmentFeature.CatalogCmMultipleOf15s]);
    }

    private static IReadOnlyList<SegmentPiece> Pieces(BoundaryCandidate[] candidates)
        => SegmentFeatures.Of(candidates, new SyntheticReadings(Length).Evidence(), Context(), null, SegmentThresholds.Initial);

    private static BoundaryCandidate Edge(double at)
        => new(TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at), BoundaryCue.RecordingEdge, 0, TimeSpan.Zero);

    private static BoundaryCandidate Cut(double at, double? latest = null, double depth = 20, double lasting = 0.5)
        => new(
            TimeSpan.FromSeconds(at),
            TimeSpan.FromSeconds(at),
            TimeSpan.FromSeconds(latest ?? at),
            BoundaryCue.Silence | BoundaryCue.SilentCut | BoundaryCue.SceneChange,
            depth,
            TimeSpan.FromSeconds(lasting));

    private static BoundaryCandidate Plain(double at)
        => new(TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at), BoundaryCue.SceneChange, 0, TimeSpan.Zero);

    private static SegmentContext Context(
        TimeSpan? starts = null,
        TimeSpan? ends = null,
        IReadOnlyList<CatalogHeard>? catalog = null,
        IReadOnlyList<PastEpisode>? past = null,
        IReadOnlyList<ProgrammeGenre>? genres = null)
        => new(new NetworkId(1), new ServiceId(1), "架空の物語", starts, ends, genres ?? [], catalog ?? [], CornerLogo.None, past ?? []);

    private static PastEpisode Episode(double programmeStarts, params (double Starts, double Ends)[] breaks)
        => new(TimeSpan.FromSeconds(programmeStarts), [.. breaks.Select(gap => new SegmentSpan(TimeSpan.FromSeconds(gap.Starts), TimeSpan.FromSeconds(gap.Ends), SegmentKind.Break, null))]);

    private static CatalogEntry Entry(CatalogKind kind)
        => CatalogEntry.Rehydrate(
            CatalogEntryId.New(),
            kind,
            kind is CatalogKind.Unknown ? null : 0.9,
            kind is CatalogKind.Unknown ? null : CatalogDecision.Pattern,
            TimeSpan.FromSeconds(15),
            new byte[sizeof(uint)],
            3,
            2,
            1,
            Evening,
            Evening,
            Evening);

    private static CatalogOccurrence Occurrence(CatalogEntry entry, SegmentKind? kind)
        => CatalogOccurrence.Rehydrate(
            1,
            entry.Id,
            RecordingId.New(),
            TimeSpan.FromSeconds(100),
            TimeSpan.FromSeconds(115),
            new NetworkId(1),
            new ServiceId(1),
            "架空の物語",
            Evening,
            0.1f,
            kind,
            Evening);
}
