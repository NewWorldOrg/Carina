using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentFeaturesTests
{
    private const double Length = 1800;

    private static readonly DateTime Evening = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    [Theory(DisplayName = "a piece is measured by its length, how far it lies off the 15-second grid, and whether it lies within the tolerance of a multiple of 15 seconds from 15 seconds to 3 minutes, on either side of it")]
    [InlineData(60.7, 0.7, 1)]
    [InlineData(60.9, 0.9, 1)]
    [InlineData(61.1, 1.1, 0)]
    [InlineData(62.5, 2.5, 0)]
    [InlineData(20, 5, 0)]
    [InlineData(14.5, 0.5, 1)]
    [InlineData(14.1, 0.9, 1)]
    [InlineData(13.9, 1.1, 0)]
    [InlineData(180, 0, 1)]
    [InlineData(180.9, 0.9, 1)]
    [InlineData(181.1, 1.1, 0)]
    [InlineData(195, 0, 0)]
    public void APieceIsMeasuredAgainstTheGrid(double seconds, double offTheGrid, double multiple)
    {
        SegmentFeatureValues features = Pieces([Edge(0), Cut(300), Cut(300 + seconds), Edge(Length)])[1].Features;

        Assert.Equal(seconds, features[SegmentFeature.Length]!.Value, 9);
        Assert.Equal(offTheGrid, features[SegmentFeature.GridOffset]!.Value, 9);
        Assert.Equal(multiple, features[SegmentFeature.IsMultipleOf15s]);
    }

    [Theory(DisplayName = "BR-CD-007: a piece is a multiple of 15 seconds as long as an advert when it is one of at most a minute, within the tolerance, since a pod is cut at every advert")]
    [InlineData(15, 1, 1)]
    [InlineData(60, 1, 1)]
    [InlineData(60.9, 1, 1)]
    [InlineData(74.1, 1, 0)]
    [InlineData(90, 1, 0)]
    [InlineData(62.5, 0, 0)]
    public void APieceIsAMultipleAsLongAsAnAdvert(double seconds, double multiple, double advert)
    {
        SegmentFeatureValues features = Pieces([Edge(0), Cut(300), Cut(300 + seconds), Edge(Length)])[1].Features;

        Assert.Equal((multiple, advert), (features[SegmentFeature.IsMultipleOf15s], features[SegmentFeature.IsShortMultipleOf15s]));
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
        Assert.Equal([null, SegmentKind.Break, null, SegmentKind.Opening, null], hearing.Select(piece => piece.Fixed?.Kind));
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

    [Theory(DisplayName = "BR-CD-007: a stretch of an opening's or an ending's sound taken for the programme where it lies counts as nothing and fixes no piece, since the song may be sung in the programme there or advertised between adverts; one taken for an ending, or of a fixed part of a programme, counts as before")]
    [InlineData(CatalogKind.Opening, SegmentKind.Programme, null)]
    [InlineData(CatalogKind.Ending, SegmentKind.Programme, null)]
    [InlineData(CatalogKind.Opening, SegmentKind.Ending, SegmentKind.Ending)]
    [InlineData(CatalogKind.ProgrammeFixture, null, SegmentKind.Programme)]
    public void AThemeTakenForTheProgrammeCountsAsNothing(CatalogKind kind, SegmentKind? alone, SegmentKind? counts)
    {
        CatalogEntry entry = Entry(kind);
        CatalogHeard heard = CatalogHeard.Of(Occurrence(entry, alone), entry);
        SegmentContext context = Context(catalog: [heard]);

        IReadOnlyList<SegmentPiece> pieces = SegmentFeatures.Of([Edge(0), Cut(100), Cut(115), Edge(Length)], new SyntheticReadings(Length).Evidence(), context, null, SegmentThresholds.Initial);

        Assert.Equal(counts, heard.Kind);
        Assert.Equal(counts, pieces[1].Fixed?.Kind);
    }

    [Fact(DisplayName = "BR-CD-007: a piece is fixed to the kind the catalog's stretches of that kind cover more than half of, a fixed part of a programme to the programme, at the confidence of the surest entry over it; a kind or a confidence not known and half or less fix nothing")]
    public void ThePiecesTheCatalogCoversAreFixed()
    {
        SegmentContext context = Context(catalog:
        [
            Heard(300, 320, SegmentKind.Break, 0.5),
            Heard(320, 345, SegmentKind.Break, 0.9),
            Heard(400, 430, SegmentKind.Break, 0.9),
            Heard(500, 590, SegmentKind.Opening, 0.8),
            Heard(700, 760, SegmentKind.Programme, 0.7),
            Heard(800, 860, null),
            Heard(900, 960, SegmentKind.Break),
            Heard(1000, 1090, SegmentKind.Ending, 0.8),
        ]);

        IReadOnlyList<SegmentPiece> pieces = SegmentFeatures.Of(
            [Edge(0), .. new double[] { 300, 360, 400, 460, 500, 590, 700, 760, 800, 860, 900, 960, 1000, 1090 }.Select(at => Cut(at)), Edge(Length)],
            new SyntheticReadings(Length).Evidence(),
            context,
            null,
            SegmentThresholds.Initial);

        Assert.Equal(
            [
                null, new CatalogFix(SegmentKind.Break, 0.9), null, null, null, new CatalogFix(SegmentKind.Opening, 0.8), null, new CatalogFix(SegmentKind.Programme, 0.7),
                null, null, null, null, null, new CatalogFix(SegmentKind.Ending, 0.8), null,
            ],
            pieces.Select(piece => piece.Fixed));
    }

    [Fact(DisplayName = "BR-CD-007: what one stretch alone is taken for fixes its piece, so a stretch of an opening heard in another series' break fixes it to a break")]
    public void AStretchTakenForABreakFixesItsPieceToABreak()
    {
        CatalogEntry opening = Entry(CatalogKind.Opening);
        SegmentContext context = Context(catalog: [CatalogHeard.Of(Occurrence(opening, SegmentKind.Break), opening)]);

        IReadOnlyList<SegmentPiece> pieces = SegmentFeatures.Of([Edge(0), Cut(100), Cut(115), Edge(Length)], new SyntheticReadings(Length).Evidence(), context, null, SegmentThresholds.Initial);

        Assert.Equal([null, new CatalogFix(SegmentKind.Break, 0.9), null], pieces.Select(piece => piece.Fixed));
    }

    [Fact(DisplayName = "BR-CD-007: of two kinds that each cover more than half of a piece, the one heard surer fixes it, and the earlier kind on a tie")]
    public void TheSurerKindFixesAPiece()
    {
        BoundaryCandidate[] candidates = [Edge(0), Cut(300), Cut(390), Edge(Length)];
        SyntheticReadings readings = new(Length);

        SegmentPiece surerOpening = SegmentFeatures.Of(candidates, readings.Evidence(), Context(catalog: [Heard(300, 390, SegmentKind.Opening, 0.8), Heard(300, 360, SegmentKind.Break, 0.5)]), null, SegmentThresholds.Initial)[1];
        SegmentPiece tie = SegmentFeatures.Of(candidates, readings.Evidence(), Context(catalog: [Heard(300, 390, SegmentKind.Opening, 0.9), Heard(300, 360, SegmentKind.Break, 0.9)]), null, SegmentThresholds.Initial)[1];

        Assert.Equal(new CatalogFix(SegmentKind.Opening, 0.8), surerOpening.Fixed);
        Assert.Equal(new CatalogFix(SegmentKind.Break, 0.9), tie.Fixed);
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

    [Fact(DisplayName = "BR-CD-007: the past opening share is how many earlier episodes had an opening within 5 seconds of the same place from their start, the past ending share how many had an ending as far from their end, each episode as the version detected it, so an opening that ended 25 seconds before the place says nothing of it")]
    public void ThePastOpeningAndEndingSharesComeFromEarlierEpisodes()
    {
        PastEpisode[] past =
        [
            Remembered(10, 1810, [(70, 160, SegmentKind.Opening), (1660, 1750, SegmentKind.Ending)]),
            Remembered(0, null, [(0, 400, SegmentKind.Programme), (400, 490, SegmentKind.Opening), (490, 1700, SegmentKind.Programme), (1700, 1790, SegmentKind.Ending), (1790, 1900, SegmentKind.Programme)]),
            Remembered(0, 1800, [(0, 1800, SegmentKind.Programme)]),
            Remembered(0, 1800, [(0, 60, SegmentKind.Programme), (60, 150, SegmentKind.Opening), (150, 1800, SegmentKind.Programme)]),
            Remembered(0, 1800, [(0, 80, SegmentKind.Opening), (80, 1800, SegmentKind.Programme)]),
        ];
        BoundaryCandidate[] candidates = [Edge(0), Cut(60), Cut(150), Cut(1650), Cut(1740), Edge(Length)];
        SegmentContext context = Context(starts: TimeSpan.Zero, ends: TimeSpan.FromSeconds(Length), past: past);

        IReadOnlyList<SegmentPiece> pieces = SegmentFeatures.Of(candidates, new SyntheticReadings(Length).Evidence(), context, null, SegmentThresholds.Initial);

        Assert.Equal(2d / 5, pieces[1].Features[SegmentFeature.PastOpeningShare]!.Value, 9);
        Assert.Equal(2d / 5, pieces[3].Features[SegmentFeature.PastEndingShare]!.Value, 9);
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

    [Fact(DisplayName = "BR-CD-008: an earlier episode whose stretches reach a few milliseconds or a second and a half past an entry set aside keeps no sliver of them, so the past shares there fall to nothing")]
    public void AnEarlierEpisodeKeepsNoSliverPastAnEntrySetAside()
    {
        CatalogSeries story = new(1, "架空の物語", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));
        RecordingId earlier = RecordingId.New();
        CatalogEntryId opening = CatalogEntryId.New();
        CatalogEntryId advert = CatalogEntryId.New();
        LearningCatalog catalog = new(
        [
            new CatalogSound(opening, TimeSpan.FromSeconds(90), [CatalogSeries.At(opening, story.Episode(0), 0, 90, earlier), CatalogSeries.At(opening, story.Episode(1), 0, 90)]),
            new CatalogSound(advert, TimeSpan.FromSeconds(30), [CatalogSeries.At(advert, story.Episode(0), 900, 30, earlier), CatalogSeries.At(advert, story.Episode(2), 900, 30)]),
        ],
        SeriesLinks.None);
        PastEpisode episode = new(
            earlier,
            CatalogSeries.Lead,
            null,
            [.. new[] { (0.0, 59.995, SegmentKind.Programme), (59.995, 151.5, SegmentKind.Opening), (151.5, 958.5, SegmentKind.Programme), (958.5, 990.005, SegmentKind.Break), (990.005, 1800, SegmentKind.Programme) }
                .Select(stretch => new SegmentSpan(TimeSpan.FromSeconds(stretch.Item1), TimeSpan.FromSeconds(stretch.Item2), stretch.Item3, 0.9))]);
        BoundaryCandidate[] candidates = [Edge(0), Cut(60), Cut(150), Cut(960), Cut(990), Edge(Length)];
        SegmentContext context = Context(starts: CatalogSeries.Lead, ends: TimeSpan.FromSeconds(Length), past: [episode]);

        SegmentContext forgetting = catalog.Forgetting(context, new HashSet<CatalogEntryId> { opening, advert });
        IReadOnlyList<SegmentPiece> remembered = SegmentFeatures.Of(candidates, new SyntheticReadings(Length).Evidence(), context, null, SegmentThresholds.Initial);
        IReadOnlyList<SegmentPiece> forgotten = SegmentFeatures.Of(candidates, new SyntheticReadings(Length).Evidence(), forgetting, null, SegmentThresholds.Initial);

        Assert.Equal((1d, 1d), (remembered[1].Features[SegmentFeature.PastOpeningShare]!.Value, remembered[3].Features[SegmentFeature.PastCmShare]!.Value));
        Assert.Equal((0d, 0d), (forgotten[1].Features[SegmentFeature.PastOpeningShare]!.Value, forgotten[3].Features[SegmentFeature.PastCmShare]!.Value));
        Assert.All(forgetting.Past.Single().Segments, segment => Assert.Equal(SegmentKind.Programme, segment.Kind));
    }

    [Fact(DisplayName = "BR-CD-008: an earlier episode forgets only the part of a stretch it was taken to hold that lies over a stretch of an entry set aside, and keeps the rest of it as it was")]
    public void AnEarlierEpisodeForgetsOnlyThePartOverAnEntrySetAside()
    {
        PastEpisode episode = new(
            RecordingId.New(),
            CatalogSeries.Lead,
            null,
            [
                new SegmentSpan(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(60), SegmentKind.Programme, 0.9),
                new SegmentSpan(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(150), SegmentKind.Break, 0.8),
                new SegmentSpan(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(960), SegmentKind.Programme, 0.9),
                new SegmentSpan(TimeSpan.FromSeconds(960), TimeSpan.FromSeconds(1000), SegmentKind.Break, null),
            ]);
        SoundSpan[] setAside = [new(TimeSpan.FromSeconds(140), TimeSpan.FromSeconds(170)), new(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(120)), new(TimeSpan.FromSeconds(950), TimeSpan.FromSeconds(1010))];

        PastEpisode forgetting = episode.Without(setAside);

        Assert.Equal(
            [(0d, 60d, SegmentKind.Programme, (double?)0.9), (60, 90, SegmentKind.Break, 0.8), (120, 140, SegmentKind.Break, 0.8), (170, 950, SegmentKind.Programme, 0.9)],
            forgetting.Segments.Select(segment => (segment.Starts.TotalSeconds, segment.Ends.TotalSeconds, segment.Kind, segment.Probability)));
        Assert.True(forgetting.HadABreakNear(TimeSpan.FromSeconds(130) - CatalogSeries.Lead, TimeSpan.Zero));
        Assert.False(forgetting.HadABreakNear(TimeSpan.FromSeconds(105) - CatalogSeries.Lead, TimeSpan.Zero));
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

    [Theory(DisplayName = "BR-CD-007: a piece starts where an opening does from 15 seconds before the guide's start to 6 minutes after it, and ends where an ending does from 6 minutes before the guide's end to 15 seconds after it")]
    [InlineData(44.9, 0, 0)]
    [InlineData(45, 1, 0)]
    [InlineData(420, 1, 0)]
    [InlineData(420.1, 0, 0)]
    [InlineData(1289.9, 0, 0)]
    [InlineData(1290, 0, 1)]
    [InlineData(1665, 0, 1)]
    [InlineData(1665.1, 0, 0)]
    public void APieceLiesWhereAThemeDoes(double starts, double opening, double ending)
    {
        SegmentContext context = Context(starts: TimeSpan.FromSeconds(60), ends: TimeSpan.FromSeconds(1740));

        SegmentFeatureValues features = SegmentFeatures.Of([Edge(0), Cut(starts), Cut(starts + 90), Edge(Length)], new SyntheticReadings(Length).Evidence(), context, null, SegmentThresholds.Initial)[1].Features;

        Assert.Equal((opening, ending), (features[SegmentFeature.OpeningWindow], features[SegmentFeature.EndingWindow]));
    }

    [Fact(DisplayName = "BR-CD-007: with nothing from the guide the programme spans the recording, so a piece starting in its first 6 minutes lies where an opening does and one ending in its last 6 where an ending does")]
    public void WithoutTheGuideThemesLieByTheRecording()
    {
        IReadOnlyList<SegmentPiece> pieces = Pieces([Edge(0), Cut(300), Cut(390), Cut(1500), Cut(1590), Edge(Length)]);

        Assert.Equal([1d, 1, 0, 0, 0], pieces.Select(piece => piece.Features[SegmentFeature.OpeningWindow]));
        Assert.Equal([0d, 0, 1, 1, 1], pieces.Select(piece => piece.Features[SegmentFeature.EndingWindow]));
    }

    [Theory(DisplayName = "BR-CD-007: a piece lasts as long as a whole opening or ending from 55 seconds to 125")]
    [InlineData(54.9, 0)]
    [InlineData(55, 1)]
    [InlineData(90, 1)]
    [InlineData(125, 1)]
    [InlineData(125.1, 0)]
    public void APieceLastsAsLongAsATheme(double seconds, double theme)
        => Assert.Equal(theme, Pieces([Edge(0), Cut(300), Cut(300 + seconds), Edge(Length)])[1].Features[SegmentFeature.IsThemeLength]);

    [Fact(DisplayName = "BR-CD-007: the loudness range is the decibels between the 10th and the 90th percentile of the momentary loudness inside a piece away from its edges, nothing for a song that keeps its loudness and 20 for speech that keeps stopping, the silence of the cuts at its edges left out")]
    public void TheLoudnessRangeTellsASongFromSpeech()
    {
        SyntheticReadings readings = new SyntheticReadings(Length).Spoken(600, 1200);

        foreach (double cut in new[] { 300.0, 390, 600, 690 })
        {
            readings.QuietAround(cut);
        }

        IReadOnlyList<SegmentPiece> pieces = SegmentFeatures.Of([Edge(0), Cut(300), Cut(390), Cut(600), Cut(690), Edge(Length)], readings.Evidence(), Context(), null, SegmentThresholds.Initial);

        Assert.Equal(0, pieces[1].Features[SegmentFeature.LoudnessRange]!.Value, 6);
        Assert.Equal(20, pieces[3].Features[SegmentFeature.LoudnessRange]!.Value, 6);
    }

    [Fact(DisplayName = "BR-CD-007: the loudness range keeps away from each edge by half the silence there, so a song after a silence of six seconds is read without it and keeps its loudness")]
    public void TheLoudnessRangeKeepsAwayFromALongSilenceAtAnEdge()
    {
        SegmentEvidence evidence = new SyntheticReadings(Length).QuietAround(300, 6).QuietAround(320).Evidence();

        IReadOnlyList<SegmentPiece> pieces = SegmentFeatures.Of([Edge(0), Cut(300, lasting: 6), Cut(320), Edge(Length)], evidence, Context(), null, SegmentThresholds.Initial);

        Assert.Equal(0, pieces[1].Features[SegmentFeature.LoudnessRange]!.Value, 6);
    }

    [Fact(DisplayName = "BR-CD-007: a piece is an advert beside another when it runs from one cut between adverts to the next, a silence of half a second or more as quiet as -90 dBFS, as long as an advert, and so does the stretch to the cut before it or after it; a lone advert, or one between cuts at a silence that is shallow, is not")]
    public void AnAdvertBesideAnotherIsOneOfAPod()
    {
        IReadOnlyList<SegmentPiece> pieces = Pieces(
        [
            Edge(0),
            Cut(600, depth: 70, lasting: 1),
            Cut(615, depth: 70, lasting: 1),
            Cut(630, depth: 70, lasting: 1),
            Cut(900, depth: 70, lasting: 1),
            Cut(915, depth: 70, lasting: 1),
            Cut(1200, depth: 10, lasting: 1),
            Cut(1215, depth: 10, lasting: 1),
            Cut(1230, depth: 10, lasting: 1),
            Edge(Length),
        ]);

        Assert.Equal([0d, 1, 1, 0, 0, 0, 0, 0, 0], pieces.Select(piece => piece.Features[SegmentFeature.AdvertBeside]!.Value));
    }

    [Fact(DisplayName = "BR-CD-007: neighbouring segments are read as one advert where the cuts of a pod hold them, across the logo coming and going and a shallow silence at a change of scene, and as one opening between two silences where an opening lies, across the logo coming and going; neither inside a lone advert, across a seam, from where only the logo comes or goes, nor over a segment the catalog fixes")]
    public void NeighbouringSegmentsAreReadAsOne()
    {
        BoundaryCandidate[] candidates =
        [
            Edge(0),
            Cut(60, depth: 70, lasting: 1),
            Plain(75),
            Plain(120),
            Cut(150, depth: 70, lasting: 1),
            Cut(600, depth: 70, lasting: 1),
            Cut(615, depth: 70, lasting: 1),
            Plain(620),
            Cut(624, depth: 10, lasting: 0.4),
            Cut(630, depth: 70, lasting: 1),
            Cut(645, depth: 70, lasting: 1),
            Cut(900, depth: 70, lasting: 1),
            Plain(905),
            Cut(915, depth: 70, lasting: 1),
            Edge(Length),
        ];
        SegmentEvidence evidence = new SyntheticReadings(Length).Evidence();

        (IReadOnlyList<SegmentPiece> pieces, IReadOnlyList<JoinedPiece> joined) = SegmentFeatures.Read(candidates, evidence, Context(), null, SegmentThresholds.Initial);
        IReadOnlyList<JoinedPiece> fixedOver = SegmentFeatures.Read(candidates, evidence, Context(catalog: [Heard(620, 624, SegmentKind.Break, 0.9)]), null, SegmentThresholds.Initial).Joined;

        Assert.Equal(
            [(60d, 150d, SegmentKind.Opening), (615d, 630d, SegmentKind.Break)],
            joined.Select(piece => (piece.Piece.Starts.TotalSeconds, piece.Piece.Ends.TotalSeconds, piece.Kinds.Single())));
        Assert.Equal([(1, 3), (6, 8)], joined.Select(piece => (piece.First, piece.Last)));
        Assert.Equal(1, joined[1].Piece.Features[SegmentFeature.AdvertBeside]);
        Assert.Equal([false, true, false, false, true, true, true, false, false, true, true, true, false, true], pieces.Select(piece => piece.AfterASeam));
        Assert.Equal([(60d, 150d, SegmentKind.Opening)], fixedOver.Select(piece => (piece.Piece.Starts.TotalSeconds, piece.Piece.Ends.TotalSeconds, piece.Kinds.Single())));
    }

    [Fact(DisplayName = "BR-CD-007: a piece shorter than 12 seconds has its loudness range read over the 10 seconds around its middle, a few seconds saying little, and it is not known where no window fits in the recording")]
    public void AShortPieceIsReadAroundItsMiddle()
    {
        SegmentEvidence evidence = new SyntheticReadings(Length).Spoken(600, 1200).Evidence();

        IReadOnlyList<SegmentPiece> pieces = SegmentFeatures.Of(
            [Edge(0), Cut(300), Cut(305), Cut(900), Cut(904), Edge(Length)],
            evidence,
            Context(),
            null,
            SegmentThresholds.Initial);
        SegmentFeatureValues around = SegmentFeatures.Of([Edge(0), Cut(895), Cut(909), Edge(Length)], evidence, Context(), null, SegmentThresholds.Initial)[1].Features;

        Assert.Equal(0, pieces[1].Features[SegmentFeature.LoudnessRange]!.Value, 6);
        Assert.Equal(20, pieces[3].Features[SegmentFeature.LoudnessRange]!.Value, 6);
        Assert.Equal(20, around[SegmentFeature.LoudnessRange]!.Value, 6);
        Assert.Null(SegmentFeatures.Of([Edge(0), Edge(0.3)], new SyntheticReadings(0.3).Evidence(), Context(), null, SegmentThresholds.Initial)[0].Features[SegmentFeature.LoudnessRange]);
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
