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

    [Theory(DisplayName = "a stretch the catalog heard as a break is one even when changes of scene cut it into pieces off the grid, and one heard as programme or as nothing known is not")]
    [InlineData(SegmentKind.Break, SegmentDetectionState.Marked)]
    [InlineData(SegmentKind.Programme, SegmentDetectionState.NothingFound)]
    [InlineData(null, SegmentDetectionState.NothingFound)]
    public void TheCatalogFindsABreakCutIntoPieces(SegmentKind? heardAs, SegmentDetectionState state)
    {
        SegmentEvidence evidence = Cuts(new SyntheticReadings(Length), 600, 630).Cut(607, 0.9).Cut(615, 0.9).Cut(622, 0.9).Evidence();
        SegmentContext context = Context(CornerLogo.None, [new CatalogHeard(TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(630), heardAs, 4, 2, 2)]);

        SegmentDetection detection = SegmentDetector.Detect(evidence, context, SegmentModel.First);

        Assert.Equal(state, detection.State);
        Assert.Equal(heardAs is SegmentKind.Break ? 30d / 1800 : 0, detection.CmShare, 9);
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
            [SegmentFeature.CatalogCmShare] = new(0.1, 0.2),
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
