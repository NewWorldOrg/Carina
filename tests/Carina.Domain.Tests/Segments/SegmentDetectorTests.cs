using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using static Carina.Domain.Tests.Segments.SyntheticCorners;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentDetectorTests
{
    private const double Length = 1800;

    private static readonly DateTime Evening = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private static readonly SeriesMember Judged = Member(Evening);

    private static readonly CornerLogo Logo = CornerLogo.Learn(
        Judged,
        [
            new RecordedCorners(Member(Evening.AddDays(-7)), Drawn(600, TopRight, second => second % 300 >= 60, 1)),
            new RecordedCorners(Member(Evening.AddDays(-1)), Drawn(600, TopRight, second => second % 300 >= 60, 2)),
        ]);

    [Fact(DisplayName = "a recording that looks like a public broadcaster's, cut off the grid and under its logo, is nothing found with no share taken by breaks")]
    public void APublicBroadcastersRecordingHasNoBreaks()
    {
        SegmentEvidence evidence = Cuts(new SyntheticReadings(Length), 137, 263.5, 371, 512.5, 600, 660, 777, 1033.5, 1290, 1601, 1700)
            .Corners(Drawn((int)Length, TopRight, second => second < 1500, 5))
            .Evidence();

        SegmentDetection detection = SegmentDetector.Detect(evidence, Context(Logo), SegmentModel.First);

        Assert.Equal(SegmentDetectionState.NothingFound, detection.State);
        Assert.Equal(0, detection.CmShare);
        Assert.DoesNotContain(detection.Segments, segment => segment.Kind is SegmentKind.Break);
    }

    [Fact(DisplayName = "a recording that looks like home shopping, a silent cut every minute and no logo, is discarded, keeping its sequence and the share")]
    public void HomeShoppingIsDiscarded()
    {
        SegmentDetection detection = SegmentDetector.Detect(
            Cuts(new SyntheticReadings(Length), [.. Enumerable.Range(1, 29).Select(minute => minute * 60.0)]).Evidence(),
            Context(CornerLogo.None),
            SegmentModel.First);

        Assert.Equal(SegmentDetectionState.Discarded, detection.State);
        Assert.Equal(1680d / 1800, detection.CmShare, 9);
        Assert.Contains(detection.Segments, segment => segment.Kind is SegmentKind.Break);
    }

    [Fact(DisplayName = "breaks taking exactly half the recording are marked, not discarded")]
    public void ExactlyHalfIsMarked()
    {
        SegmentDetection detection = SegmentDetector.Detect(Half(), Context(CornerLogo.None), SegmentModel.First);

        Assert.Equal(SegmentDetectionState.Marked, detection.State);
        Assert.Equal(0.5, detection.CmShare, 12);
        Assert.Equal(5, detection.Segments.Count(segment => segment.Kind is SegmentKind.Break));
        Assert.All(detection.Segments.Where(segment => segment.Kind is SegmentKind.Break), segment => Assert.Equal(TimeSpan.FromSeconds(180), segment.Length));
    }

    [Fact(DisplayName = "a recording with more than 80 stretches an hour is discarded, and one with fewer is marked")]
    public void TooManyStretchesAnHourAreDiscarded()
    {
        SegmentDetection twenty = SegmentDetector.Detect(Pods(20), Context(CornerLogo.None), SegmentModel.First);
        SegmentDetection nineteen = SegmentDetector.Detect(Pods(19), Context(CornerLogo.None), SegmentModel.First);

        Assert.Equal(SegmentDetectionState.Discarded, twenty.State);
        Assert.Equal(41, twenty.Segments.Count);
        Assert.Equal(300d / 1800, twenty.CmShare, 9);
        Assert.Equal(SegmentDetectionState.Marked, nineteen.State);
        Assert.Equal(39, nineteen.Segments.Count);
    }

    [Theory(DisplayName = "a stretch the catalog heard as a break is one even without silence at its edges, and one heard as programme or as nothing known is not")]
    [InlineData(SegmentKind.Break, SegmentDetectionState.Marked)]
    [InlineData(SegmentKind.Programme, SegmentDetectionState.NothingFound)]
    [InlineData(null, SegmentDetectionState.NothingFound)]
    public void TheCatalogFindsABreakWithoutSilence(SegmentKind? heardAs, SegmentDetectionState state)
    {
        SegmentContext context = Context(CornerLogo.None, [new CatalogHeard(TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(630), heardAs, 4, 2, 2)]);

        SegmentDetection detection = SegmentDetector.Detect(new SyntheticReadings(Length).Evidence(), context, SegmentModel.First);

        Assert.Equal(state, detection.State);
        Assert.Equal(heardAs is SegmentKind.Break ? 30d / 1800 : 0, detection.CmShare, 9);
    }

    [Fact(DisplayName = "BR-CD-007: a stretch the catalog hears as an advert is a break at 0.9 even under the logo with nothing silent, where the version made by hand alone takes it for the programme")]
    public void TheCatalogFixesABreakUnderTheLogo()
    {
        SegmentEvidence evidence = new SyntheticReadings(Length).Corners(Drawn((int)Length, TopRight, _ => true, 3)).Evidence();
        CatalogHeard advert = new(TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(630), SegmentKind.Break, 4, 2, 2, 0.9);

        SegmentDetection heard = SegmentDetector.Detect(evidence, Context(Logo, [advert]), SegmentModel.First);
        RecordingPieces read = SegmentDetector.Read(evidence, Context(Logo, [advert]), SegmentThresholds.Initial);
        SegmentPiece covered = read.Pieces.Single(piece => piece.Fixed is SegmentKind.Break);
        SegmentSpan found = heard.Segments.Single(segment => segment.Kind is SegmentKind.Break);

        Assert.True(SegmentModel.First.Chances(covered.Features, read.Station, read.Series).Of(SegmentKind.Break) < 0.5);
        Assert.Equal((TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(630), SegmentSequence.FixedBreakChance), (found.Starts, found.Ends, found.Probability!.Value));
    }

    [Fact(DisplayName = "an advert of 15 seconds with two changes of scene inside it and nothing from the catalog is one break of 15 seconds")]
    public void ChangesOfSceneInsideAnAdvertDoNotCutIt()
    {
        SegmentEvidence evidence = Cuts(new SyntheticReadings(Length), 600, 615).Cut(604, 0.9).Cut(610, 0.9).Evidence();

        SegmentDetection detection = SegmentDetector.Detect(evidence, Context(CornerLogo.None), SegmentModel.First);
        SegmentSpan advert = detection.Segments.Single(segment => segment.Kind is SegmentKind.Break);

        Assert.Equal(SegmentDetectionState.Marked, detection.State);
        Assert.Equal((TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(615)), (advert.Starts, advert.Ends));
    }

    [Fact(DisplayName = "programme with captions coming and going is one stretch of programme, cut nowhere by the captions")]
    public void CaptionsComingAndGoingDoNotCutTheProgramme()
    {
        SegmentEvidence evidence = Cuts(new SyntheticReadings(Length), 600, 615).Captioned(second => second < 600 && second % 9 < 5).Evidence();

        SegmentDetection detection = SegmentDetector.Detect(evidence, Context(CornerLogo.None), SegmentModel.First);

        Assert.Equal([TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(615)], detection.Snaps);
        Assert.Equal((TimeSpan.Zero, TimeSpan.FromSeconds(600), SegmentKind.Programme), (detection.Segments[0].Starts, detection.Segments[0].Ends, detection.Segments[0].Kind));
    }

    [Fact(DisplayName = "the logo learned from the station's earlier recordings takes away a pod it stays on screen through")]
    public void TheLogoTakesAwayAPodItStaysOnThrough()
    {
        SyntheticReadings branded = Cuts(new SyntheticReadings(Length), 600, 660).Corners(Drawn((int)Length, TopRight, second => second is not (>= 1000 and < 1300), 6));
        SyntheticReadings dark = Cuts(new SyntheticReadings(Length), 600, 660).Corners(Drawn((int)Length, TopRight, second => second is not (>= 600 and < 900), 6));

        Assert.Equal(SegmentDetectionState.NothingFound, SegmentDetector.Detect(branded.Evidence(), Context(Logo), SegmentModel.First).State);
        Assert.Equal(SegmentDetectionState.Marked, SegmentDetector.Detect(dark.Evidence(), Context(Logo), SegmentModel.First).State);
    }

    [Fact(DisplayName = "every segment is standardised with the scales the version carries: weights rescaled with them detect the same, and the same weights under other scales do not")]
    public void StandardisationIsAppliedWithTheVersionsScales()
    {
        Dictionary<string, FeatureScale> scales = new()
        {
            [SegmentFeature.IsMultipleOf15s] = new(0.5, 0.25),
            [SegmentFeature.EdgeSilence] = new(0.2, 0.4),
            [SegmentFeature.LogoShare] = new(0, 0.3),
        };
        SegmentClassWeights first = SegmentModel.First.Weights.Classes[SegmentKind.Break];
        SegmentModel rescaled = Model(
            first.Bias + first.Features.Sum(weight => weight.Value * scales[weight.Key].Mean),
            first.Features.ToDictionary(weight => weight.Key, weight => weight.Value * scales[weight.Key].Sd),
            scales);
        SegmentModel spread = Model(first.Bias, first.Features, first.Features.Keys.ToDictionary(name => name, _ => new FeatureScale(0, 2)));
        SegmentEvidence half = Half();

        Assert.Equal(Summary(SegmentDetector.Detect(half, Context(CornerLogo.None), SegmentModel.First)), Summary(SegmentDetector.Detect(half, Context(CornerLogo.None), rescaled)));
        Assert.Equal(SegmentDetectionState.NothingFound, SegmentDetector.Detect(half, Context(CornerLogo.None), spread).State);
    }

    [Theory(DisplayName = "the offsets of the judged recording's station and series are added to every segment, and offsets kept for another station or series are not")]
    [InlineData(true, true, SegmentDetectionState.Marked)]
    [InlineData(true, false, SegmentDetectionState.NothingFound)]
    [InlineData(false, true, SegmentDetectionState.NothingFound)]
    [InlineData(false, false, SegmentDetectionState.NothingFound)]
    public void TheJudgedStationAndSeriesOffsetsAreAdded(bool judgedStation, bool judgedSeries, SegmentDetectionState state)
    {
        NetworkId network = new(32736);
        ServiceId service = new(1024);
        SegmentContext context = new(network, service, "架空の物語", null, null, [], [], CornerLogo.None, []);
        SegmentModel offset = new(
            SegmentModel.First.Weights,
            new Dictionary<string, double> { [judgedStation ? SegmentModel.StationKey(network, service) : SegmentModel.StationKey(new NetworkId(4), new ServiceId(101))] = 0.3 },
            new Dictionary<string, double> { [judgedSeries ? "架空の物語" : "別の架空の話"] = 0.3 },
            SegmentThresholds.Initial,
            SegmentModel.First.Standardisation);
        SegmentEvidence evidence = new SyntheticReadings(Length).QuietAround(600).QuietAround(660).Evidence();

        SegmentDetection first = SegmentDetector.Detect(evidence, context, SegmentModel.First);
        SegmentDetection offsetDetection = SegmentDetector.Detect(evidence, context, offset);

        Assert.Equal(SegmentDetectionState.NothingFound, first.State);
        Assert.Equal(state, offsetDetection.State);
        Assert.Equal(state is SegmentDetectionState.Marked ? 60d / 1800 : 0, offsetDetection.CmShare, 9);
    }

    [Fact(DisplayName = "the same readings and context give the same detection every time, whatever order the catalog is handed over in")]
    public void TheSameInputGivesTheSameDetection()
    {
        SegmentEvidence evidence = Cuts(new SyntheticReadings(Length), 300, 360, 600, 630, 900, 1020).Cut(607, 0.9).Cut(615, 0.9)
            .Corners(Drawn((int)Length, TopRight, second => second is not ((>= 300 and < 360) or (>= 900 and < 1020)), 7))
            .Captioned(second => second % 10 < 6)
            .Channels(900, 1020, 100)
            .Evidence();
        CatalogHeard[] catalog =
        [
            new(TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(630), SegmentKind.Break, 4, 2, 2),
            new(TimeSpan.FromSeconds(1500), TimeSpan.FromSeconds(1510), SegmentKind.Programme, 3, 1, 1),
        ];

        SegmentDetection once = SegmentDetector.Detect(evidence, Context(Logo, catalog), SegmentModel.First);
        SegmentDetection again = SegmentDetector.Detect(evidence, Context(Logo, [.. catalog.Reverse()]), SegmentModel.First);

        Assert.Equal(Summary(once), Summary(again));
        Assert.Equal(once.Snaps, again.Snaps);
        Assert.Equal(SegmentDetectionState.Marked, once.State);
    }

    [Fact(DisplayName = "nothing is detected without readings, a context and a model")]
    public void NothingIsDetectedWithoutWhatItReads()
    {
        SegmentEvidence evidence = new SyntheticReadings(Length).Evidence();

        Assert.Throws<ArgumentNullException>(() => SegmentDetector.Detect(null!, Context(CornerLogo.None), SegmentModel.First));
        Assert.Throws<ArgumentNullException>(() => SegmentDetector.Detect(evidence, null!, SegmentModel.First));
        Assert.Throws<ArgumentNullException>(() => SegmentDetector.Detect(evidence, Context(CornerLogo.None), null!));
    }

    private static SegmentEvidence Half() => Cuts(new SyntheticReadings(Length), 100, 280, 467, 647, 834, 1014, 1201, 1381, 1568, 1748).Evidence();

    private static SegmentEvidence Pods(int many)
        => Cuts(new SyntheticReadings(Length), [.. Enumerable.Range(0, many).SelectMany(pod => new[] { 40 + (85.0 * pod), 55 + (85.0 * pod) })]).Evidence();

    private static SyntheticReadings Cuts(SyntheticReadings readings, params double[] at)
    {
        foreach (double second in at)
        {
            readings.QuietAround(second).Cut(second, 0.9);
        }

        return readings;
    }

    private static string Summary(SegmentDetection detection)
        => $"{detection.State} {detection.CmShare:F12} {string.Join(" ", detection.Segments.Select(segment => $"{segment.Starts}-{segment.Ends}:{segment.Kind}:{segment.Probability:F9}"))}";

    private static SegmentModel Model(double bias, IReadOnlyDictionary<string, double> weights, IReadOnlyDictionary<string, FeatureScale> scales)
        => new(
            new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights> { [SegmentKind.Break] = new(bias, weights) }),
            new Dictionary<string, double>(),
            new Dictionary<string, double>(),
            SegmentThresholds.Initial,
            scales);

    private static SegmentContext Context(CornerLogo logo, IReadOnlyList<CatalogHeard>? catalog = null)
        => new(Judged.NetworkId, Judged.ServiceId, Judged.SeriesName, null, null, [], catalog ?? [], logo, []);

    private static SeriesMember Member(DateTime startedAt)
        => new(RecordingId.New(), new NetworkId(1), new ServiceId(1), "架空の物語", startedAt);
}
