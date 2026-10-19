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

    [Fact(DisplayName = "BR-CD-007: no feature reads the catalog, so a piece the catalog hears has the features it has with nothing heard")]
    public void NoFeatureReadsTheCatalog()
    {
        BoundaryCandidate[] candidates = [Edge(0), Cut(300), Cut(360), Cut(500), Cut(590), Edge(Length)];
        SegmentEvidence evidence = new SyntheticReadings(Length).Captioned(second => second % 7 < 3).Evidence();
        SegmentContext heard = Context(starts: TimeSpan.FromSeconds(30), ends: TimeSpan.FromSeconds(1770), catalog: [Heard(300, 345, SegmentKind.Break, 0.9), Heard(500, 590, SegmentKind.Opening, 0.8), Heard(700, 720, SegmentKind.Programme, 0.7)]);
        SegmentContext deaf = Context(starts: TimeSpan.FromSeconds(30), ends: TimeSpan.FromSeconds(1770));

        IReadOnlyList<SegmentPiece> hearing = SegmentFeatures.Of(candidates, evidence, heard, null, SegmentThresholds.Initial);
        IReadOnlyList<SegmentPiece> hearingNothing = SegmentFeatures.Of(candidates, evidence, deaf, null, SegmentThresholds.Initial);

        Assert.DoesNotContain(SegmentFeature.Names, name => name.StartsWith("catalog", StringComparison.Ordinal));
        Assert.All(
            hearing.Zip(hearingNothing),
            pair => Assert.Equal(SegmentFeature.Names.Select(name => pair.First.Features[name]), SegmentFeature.Names.Select(name => pair.Second.Features[name])));
        Assert.Equal([null, SegmentKind.Break, null, SegmentKind.Opening, null], hearing.Select(piece => piece.Fixed));
        Assert.All(hearingNothing, piece => Assert.Null(piece.Fixed));
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

    [Fact(DisplayName = "BR-CD-007: a piece is fixed to a break, an opening or an ending the catalog's stretches of that kind cover more than half of; a programme fixture, a kind not known and half or less fix nothing")]
    public void ThePiecesTheCatalogCoversAreFixed()
    {
        SegmentContext context = Context(catalog:
        [
            Heard(300, 320, SegmentKind.Break),
            Heard(320, 345, SegmentKind.Break),
            Heard(400, 430, SegmentKind.Break),
            Heard(500, 590, SegmentKind.Opening),
            Heard(700, 760, SegmentKind.Programme),
            Heard(800, 860, null),
            Heard(1000, 1090, SegmentKind.Ending),
        ]);

        IReadOnlyList<SegmentPiece> pieces = SegmentFeatures.Of(
            [Edge(0), .. new double[] { 300, 360, 400, 460, 500, 590, 700, 760, 800, 860, 1000, 1090 }.Select(at => Cut(at)), Edge(Length)],
            new SyntheticReadings(Length).Evidence(),
            context,
            null,
            SegmentThresholds.Initial);

        Assert.Equal(
            [null, SegmentKind.Break, null, null, null, SegmentKind.Opening, null, null, null, null, null, SegmentKind.Ending, null],
            pieces.Select(piece => piece.Fixed));
    }

    [Fact(DisplayName = "BR-CD-007: what one stretch alone is taken for fixes its piece, so a stretch of an opening heard in another series' break fixes it to a break")]
    public void AStretchTakenForABreakFixesItsPieceToABreak()
    {
        CatalogEntry opening = Entry(CatalogKind.Opening);
        SegmentContext context = Context(catalog: [CatalogHeard.Of(Occurrence(opening, SegmentKind.Break), opening)]);

        IReadOnlyList<SegmentPiece> pieces = SegmentFeatures.Of([Edge(0), Cut(100), Cut(115), Edge(Length)], new SyntheticReadings(Length).Evidence(), context, null, SegmentThresholds.Initial);

        Assert.Equal([null, SegmentKind.Break, null], pieces.Select(piece => piece.Fixed));
    }

    [Fact(DisplayName = "BR-CD-007: of two kinds that each cover more than half of a piece, the one heard surer fixes it, and the earlier kind on a tie")]
    public void TheSurerKindFixesAPiece()
    {
        BoundaryCandidate[] candidates = [Edge(0), Cut(300), Cut(390), Edge(Length)];
        SyntheticReadings readings = new(Length);

        SegmentPiece surerOpening = SegmentFeatures.Of(candidates, readings.Evidence(), Context(catalog: [Heard(300, 390, SegmentKind.Opening, 0.8), Heard(300, 360, SegmentKind.Break, 0.5)]), null, SegmentThresholds.Initial)[1];
        SegmentPiece tie = SegmentFeatures.Of(candidates, readings.Evidence(), Context(catalog: [Heard(300, 390, SegmentKind.Opening, 0.9), Heard(300, 360, SegmentKind.Break, 0.9)]), null, SegmentThresholds.Initial)[1];

        Assert.Equal(SegmentKind.Opening, surerOpening.Fixed);
        Assert.Equal(SegmentKind.Break, tie.Fixed);
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

    [Fact(DisplayName = "BR-CD-007: the past opening share is how many earlier episodes had an opening within 30 seconds of the same place from their start, the past ending share how many had an ending as far from their end, each episode as the version detected it")]
    public void ThePastOpeningAndEndingSharesComeFromEarlierEpisodes()
    {
        PastEpisode[] past =
        [
            Remembered(10, 1810, [(70, 160, SegmentKind.Opening), (1660, 1750, SegmentKind.Ending)]),
            Remembered(0, null, [(0, 400, SegmentKind.Programme), (400, 490, SegmentKind.Opening), (490, 1700, SegmentKind.Programme), (1700, 1790, SegmentKind.Ending), (1790, 1900, SegmentKind.Programme)]),
            Remembered(0, 1800, [(0, 1800, SegmentKind.Programme)]),
            Remembered(0, 1800, [(0, 60, SegmentKind.Programme), (60, 150, SegmentKind.Opening), (150, 1800, SegmentKind.Programme)]),
        ];
        BoundaryCandidate[] candidates = [Edge(0), Cut(60), Cut(150), Cut(1650), Cut(1740), Edge(Length)];
        SegmentContext context = Context(starts: TimeSpan.Zero, ends: TimeSpan.FromSeconds(Length), past: past);

        IReadOnlyList<SegmentPiece> pieces = SegmentFeatures.Of(candidates, new SyntheticReadings(Length).Evidence(), context, null, SegmentThresholds.Initial);

        Assert.Equal(2d / 4, pieces[1].Features[SegmentFeature.PastOpeningShare]!.Value, 9);
        Assert.Equal(2d / 4, pieces[3].Features[SegmentFeature.PastEndingShare]!.Value, 9);
        Assert.Equal(0, pieces[3].Features[SegmentFeature.PastOpeningShare]!.Value, 9);
        Assert.Equal(0, pieces[1].Features[SegmentFeature.PastEndingShare]!.Value, 9);
        Assert.Null(Pieces([Edge(0), Cut(60), Cut(150), Edge(Length)])[1].Features[SegmentFeature.PastOpeningShare]);
        Assert.Null(Pieces([Edge(0), Cut(60), Cut(150), Edge(Length)])[1].Features[SegmentFeature.PastEndingShare]);
    }

    [Fact(DisplayName = "BR-CD-008: an earlier episode forgets what it was taken to hold over a stretch of an entry set aside, so an opening held out raises the past opening share no more, and an entry not set aside is remembered as it was")]
    public void AnEarlierEpisodeForgetsWhatLiesOverAnEntrySetAside()
    {
        CatalogSeries story = new(1, "架空の物語", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        RecordingId earlier = RecordingId.New();
        RecordingId judged = RecordingId.New();
        CatalogEntryId opening = CatalogEntryId.New();
        CatalogEntryId elsewhere = CatalogEntryId.New();
        LearningCatalog catalog = new(
        [
            new CatalogSound(opening, TimeSpan.FromSeconds(90), [CatalogSeries.At(opening, story.Episode(0), 0, 90, earlier), CatalogSeries.At(opening, story.Episode(1), 0, 90, judged)]),
            new CatalogSound(elsewhere, TimeSpan.FromSeconds(30), [CatalogSeries.At(elsewhere, story.Episode(0), 900, 30, earlier), CatalogSeries.At(elsewhere, story.Episode(2), 900, 30)]),
        ],
        SeriesLinks.None);
        PastEpisode episode = new(
            earlier,
            CatalogSeries.Lead,
            null,
            [.. new[] { (0.0, 60.0, SegmentKind.Programme), (60, 150, SegmentKind.Opening), (150, 960, SegmentKind.Programme), (960, 990, SegmentKind.Break), (990, 1800, SegmentKind.Programme) }
                .Select(stretch => new SegmentSpan(TimeSpan.FromSeconds(stretch.Item1), TimeSpan.FromSeconds(stretch.Item2), stretch.Item3, 0.9))]);
        BoundaryCandidate[] candidates = [Edge(0), Cut(60), Cut(150), Edge(Length)];
        SegmentContext context = Context(starts: CatalogSeries.Lead, ends: TimeSpan.FromSeconds(Length), past: [episode]);

        SegmentContext forgetting = catalog.Forgetting(context, new HashSet<CatalogEntryId> { opening });
        IReadOnlyList<SegmentPiece> remembered = SegmentFeatures.Of(candidates, new SyntheticReadings(Length).Evidence(), context, null, SegmentThresholds.Initial);
        IReadOnlyList<SegmentPiece> forgotten = SegmentFeatures.Of(candidates, new SyntheticReadings(Length).Evidence(), forgetting, null, SegmentThresholds.Initial);

        Assert.Equal(1, remembered[1].Features[SegmentFeature.PastOpeningShare]!.Value, 9);
        Assert.Equal(0, forgotten[1].Features[SegmentFeature.PastOpeningShare]!.Value, 9);
        Assert.Equal([SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Break, SegmentKind.Programme], forgetting.Past.Single().Segments.Select(segment => segment.Kind));
        Assert.Equal([new SoundSpan(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(150))], catalog.StretchesOf(earlier, new HashSet<CatalogEntryId> { opening }));
        Assert.Same(context, catalog.Forgetting(context, new HashSet<CatalogEntryId>()));
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

    [Fact(DisplayName = "changes of scene are counted per ten seconds of a piece, and the change in how alike the channels are is read across each edge")]
    public void ScenesAndChannelChangesAreRead()
    {
        SegmentEvidence evidence = new SyntheticReadings(Length).Cut(305, 0.9).Cut(310, 0.9).Cut(320, 0.2).Channels(360, Length, 100).Evidence();

        IReadOnlyList<SegmentPiece> pieces = SegmentFeatures.Of([Edge(0), Cut(300), Cut(360), Edge(Length)], evidence, Context(), null, SegmentThresholds.Initial);

        Assert.Equal(2d / 6, pieces[1].Features[SegmentFeature.SceneChangesPer10s]!.Value, 9);
        Assert.Equal(0, pieces[0].Features[SegmentFeature.SceneChangesPer10s]);
        Assert.Equal(0, pieces[1].Features[SegmentFeature.HeadChannelChange]!.Value, 9);
        Assert.Equal(0.9, pieces[1].Features[SegmentFeature.TailChannelChange]!.Value, 9);
        Assert.Equal(0.9, pieces[2].Features[SegmentFeature.HeadChannelChange]!.Value, 9);
        Assert.Null(pieces[0].Features[SegmentFeature.HeadChannelChange]);
        Assert.Null(pieces[2].Features[SegmentFeature.TailChannelChange]);
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

    [Fact(DisplayName = "the interaction is a multiple of 15 seconds without the logo")]
    public void TheInteractionIsAProduct()
    {
        bool[] seen = [.. Enumerable.Range(0, (int)Length).Select(second => second is >= 300 and < 312)];

        SegmentFeatureValues features = SegmentFeatures.Of([Edge(0), Cut(300), Cut(360), Edge(Length)], new SyntheticReadings(Length).Evidence(), Context(), seen, SegmentThresholds.Initial)[1].Features;
        SegmentFeatureValues off = SegmentFeatures.Of([Edge(0), Cut(300), Cut(362), Edge(Length)], new SyntheticReadings(Length).Evidence(), Context(), seen, SegmentThresholds.Initial)[1].Features;
        SegmentFeatureValues blind = SegmentFeatures.Of([Edge(0), Cut(300), Cut(360), Edge(Length)], new SyntheticReadings(Length).Evidence(), Context(), null, SegmentThresholds.Initial)[1].Features;

        Assert.Equal(1 - (9d / 55), features[SegmentFeature.MultipleOf15sWithoutLogo]!.Value, 9);
        Assert.Equal(0, off[SegmentFeature.MultipleOf15sWithoutLogo]);
        Assert.Null(blind[SegmentFeature.MultipleOf15sWithoutLogo]);
    }

    private static CatalogHeard Heard(double starts, double ends, SegmentKind? kind, double? confidence = null)
        => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends), kind, 2, 2, 1, confidence);

    private static IReadOnlyList<SegmentPiece> Pieces(BoundaryCandidate[] candidates)
        => SegmentFeatures.Of(candidates, new SyntheticReadings(Length).Evidence(), Context(), null, SegmentThresholds.Initial);

    private static BoundaryCandidate Edge(double at)
        => new(TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at), BoundaryCue.RecordingEdge, 0, TimeSpan.Zero);

    private static BoundaryCandidate Cut(double at, double? latest = null, double depth = 20, double lasting = 0.5)
        => new(
            TimeSpan.FromSeconds(at),
            TimeSpan.FromSeconds(at),
            TimeSpan.FromSeconds(latest ?? at),
            BoundaryCue.Silence | BoundaryCue.SilentCut,
            depth,
            TimeSpan.FromSeconds(lasting));

    private static BoundaryCandidate Plain(double at)
        => new(TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at), BoundaryCue.LogoChange, 0, TimeSpan.Zero);

    private static SegmentContext Context(
        TimeSpan? starts = null,
        TimeSpan? ends = null,
        IReadOnlyList<CatalogHeard>? catalog = null,
        IReadOnlyList<PastEpisode>? past = null,
        IReadOnlyList<ProgrammeGenre>? genres = null)
        => new(new NetworkId(1), new ServiceId(1), "架空の物語", starts, ends, genres ?? [], catalog ?? [], CornerLogo.None, past ?? []);

    private static PastEpisode Episode(double programmeStarts, params (double Starts, double Ends)[] breaks)
        => new(RecordingId.New(), TimeSpan.FromSeconds(programmeStarts), null, [.. breaks.Select(gap => new SegmentSpan(TimeSpan.FromSeconds(gap.Starts), TimeSpan.FromSeconds(gap.Ends), SegmentKind.Break, null))]);

    private static PastEpisode Remembered(
        double programmeStarts,
        double? programmeEnds,
        (double Starts, double Ends, SegmentKind Kind)[] detected)
        => new(
            RecordingId.New(),
            TimeSpan.FromSeconds(programmeStarts),
            programmeEnds is double ends ? TimeSpan.FromSeconds(ends) : null,
            Spans(detected));

    private static SegmentSpan[] Spans((double Starts, double Ends, SegmentKind Kind)[] stretches)
        => [.. stretches.Select(stretch => new SegmentSpan(TimeSpan.FromSeconds(stretch.Starts), TimeSpan.FromSeconds(stretch.Ends), stretch.Kind, null))];

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
            Evening,
            CatalogKinds.Version);

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
