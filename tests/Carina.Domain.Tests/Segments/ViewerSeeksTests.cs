using Carina.Domain.Channels;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class ViewerSeeksTests
{
    private static readonly DateTime Evening = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(0.5);

    private static readonly double[] Pod = [600, 615, 630, 645, 660, 675];

    private static readonly BoundaryCandidate[] AroundThePod =
    [
        Cut(0, BoundaryCue.RecordingEdge),
        Cut(520, BoundaryCue.Silence | BoundaryCue.SilentCut),
        Cut(588, BoundaryCue.Silence),
        .. Pod.Select(at => Cut(at, BoundaryCue.Silence | BoundaryCue.SilentCut)),
        Cut(688, BoundaryCue.Silence | BoundaryCue.SilentCut),
        Cut(1800, BoundaryCue.RecordingEdge),
    ];

    [Theory(DisplayName = "BR-CV-004: the ends of a seek are read as reaching eight seconds either way by the bar and the keys, ten by steps and two by the chapter button")]
    [InlineData(SeekMeans.Bar, 8)]
    [InlineData(SeekMeans.Key, 8)]
    [InlineData(SeekMeans.Step, 10)]
    [InlineData(SeekMeans.Chapter, 2)]
    public void EachMeansReachesItsOwnWay(SeekMeans means, double seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), ViewerSeeks.Reach(means));

    [Fact(DisplayName = "BR-CV-004: steps begun partway into a pod and run past its end are drawn to the cut nearest where they began and to the pod's end, not to the programme's cut after it")]
    public void StepsPastAPodAreDrawnToItsEnd()
    {
        ViewerSeeks read = ViewerSeeks.Read([Forward(SeekMeans.Step, 621, 684)], AroundThePod, null, Tolerance);

        SeekSpan span = Assert.Single(read.Spans);
        Assert.Equal((Seconds(615), Seconds(675), ViewerSeeks.MostWeight, true), (span.Starts, span.Ends, span.Weight, span.InAdvertSteps));
    }

    [Fact(DisplayName = "BR-CV-004: a jump by the bar from five seconds before a pod to six after it is drawn to the pod's two ends, past a quieter pause and a cut in the programme")]
    public void AJumpOverAPodIsDrawnToItsEnds()
    {
        ViewerSeeks read = ViewerSeeks.Read([Forward(SeekMeans.Bar, 595, 681)], AroundThePod, null, Tolerance);

        SeekSpan span = Assert.Single(read.Spans);
        Assert.Equal((Seconds(600), Seconds(675), ViewerSeeks.MostWeight, true), (span.Starts, span.Ends, span.Weight, span.InAdvertSteps));
    }

    [Fact(DisplayName = "BR-CV-004: of two cuts as strong within reach, the one an advert's length from a cut inside what was passed is taken over the nearer one in the programme")]
    public void TheCutFacingThePodIsTaken()
    {
        BoundaryCandidate[] candidates = [.. AroundThePod.Where(candidate => candidate.At != Seconds(588)), Cut(596, BoundaryCue.Silence | BoundaryCue.SilentCut)];

        ViewerSeeks read = ViewerSeeks.Read([Forward(SeekMeans.Bar, 595, 681)], candidates, null, Tolerance);

        Assert.Equal(Seconds(600), Assert.Single(read.Spans).Starts);
    }

    [Theory(DisplayName = "BR-CV-004: an end with no cut within reach is drawn in by the reach and the seek weighs less, and one with neither is kept by the stretch between the two drawn in")]
    [InlineData(595, 1300, 600, 1292, ViewerSeeks.OneEndLeftWeight)]
    [InlineData(1000, 1300, 1008, 1292, ViewerSeeks.BothEndsLeftWeight)]
    public void AnEndWithNoCutIsDrawnIn(double from, double to, double starts, double ends, double weight)
    {
        ViewerSeeks read = ViewerSeeks.Read([Forward(SeekMeans.Bar, from, to)], AroundThePod, null, Tolerance);

        SeekSpan span = Assert.Single(read.Spans);
        Assert.Equal((Seconds(starts), Seconds(ends), weight, false), (span.Starts, span.Ends, span.Weight, span.InAdvertSteps));
    }

    [Fact(DisplayName = "BR-CV-004: a seek whose start finds no cut but whose landing does keeps the landing and draws the start in")]
    public void OnlyTheLandingIsDrawn()
    {
        ViewerSeeks read = ViewerSeeks.Read([Forward(SeekMeans.Bar, 1000, 1210)], [.. AroundThePod, Cut(1205, BoundaryCue.Silence | BoundaryCue.SilentCut)], null, Tolerance);

        SeekSpan span = Assert.Single(read.Spans);
        Assert.Equal((Seconds(1008), Seconds(1205), ViewerSeeks.OneEndLeftWeight), (span.Starts, span.Ends, span.Weight));
    }

    [Theory(DisplayName = "BR-CV-004: a seek that keeps less than ten seconds once its ends are drawn is not used")]
    [InlineData(SeekMeans.Bar, 1000, 1025)]
    [InlineData(SeekMeans.Step, 1000, 1029)]
    [InlineData(SeekMeans.Chapter, 1000, 1009.9)]
    public void ASeekTooShortIsNotUsed(SeekMeans means, double from, double to)
        => Assert.Empty(ViewerSeeks.Read([Forward(means, from, to)], AroundThePod, null, Tolerance).Spans);

    [Fact(DisplayName = "BR-CV-004: the kind the version in use found over most of what was passed is kept with it")]
    public void TheVersionInUseSaysTheKind()
    {
        SegmentDetection inUse = SegmentDetection.Judged(
            [Span(0, 1002, SegmentKind.Programme), Span(1002, 1290, SegmentKind.Opening), Span(1290, 1800, SegmentKind.Programme)],
            TimeSpan.FromMinutes(30),
            []);

        ViewerSeeks read = ViewerSeeks.Read([Forward(SeekMeans.Bar, 1000, 1300)], AroundThePod, inUse, Tolerance);

        Assert.Equal(SegmentKind.Opening, Assert.Single(read.Spans).Detected);
    }

    [Fact(DisplayName = "BR-CV-004: the same stretch passed twice is one, and a seek the viewer came back from within it is not used, ten seconds from where they came back being the programme")]
    public void TheSameStretchIsOneAndComingBackUndoesIt()
    {
        ViewerSeek first = Forward(SeekMeans.Bar, 595, 681);
        ViewerSeek again = new(Evening.AddMinutes(5), SeekAction.Forward, SeekMeans.Step, Seconds(598), Seconds(683));
        ViewerSeek later = new(Evening.AddMinutes(9), SeekAction.Forward, SeekMeans.Bar, Seconds(595), Seconds(681));
        ViewerSeek back = new(Evening.AddMinutes(9).AddSeconds(20), SeekAction.Returned, SeekMeans.Key, Seconds(691), Seconds(640));

        ViewerSeeks twice = ViewerSeeks.Read([first, again], AroundThePod, null, Tolerance);
        ViewerSeeks cameBack = ViewerSeeks.Read([first, later, back], AroundThePod, null, Tolerance);

        Assert.Single(twice.Spans);
        Assert.Equal([Seconds(640)], cameBack.CameBackTo);
        Assert.Single(cameBack.Spans);
    }

    [Fact(DisplayName = "BR-CV-004: what was passed takes the catalog's kind first, then the version in use's, then a break when it runs from cut to cut in adverts' lengths, and nothing otherwise; the catalog saying programme leaves it unused")]
    public void TheKindComesFromTheCatalogThenTheVersionThenTheGrid()
    {
        SeekSpan steps = new(Seconds(600), Seconds(675), ViewerSeeks.MostWeight, null, true);
        SeekSpan opening = steps with { Detected = SegmentKind.Opening };
        SeekSpan plain = steps with { InAdvertSteps = false };
        CatalogHeard ending = new(Seconds(590), Seconds(680), SegmentKind.Ending, 3, 1, 1, 0.8);
        CatalogHeard programme = new(Seconds(590), Seconds(680), SegmentKind.Programme, 3, 1, 1, 0.7);

        Assert.Equal(
            [SegmentKind.Ending, SegmentKind.Opening, SegmentKind.Break, null, null],
            new SegmentKind?[]
            {
                ViewerSeeks.KindOf(opening, [ending]),
                ViewerSeeks.KindOf(opening, []),
                ViewerSeeks.KindOf(steps, []),
                ViewerSeeks.KindOf(plain, []),
                ViewerSeeks.KindOf(steps, [programme]),
            });
    }

    [Fact(DisplayName = "BR-CV-004: a seek labels what it passed at a tenth, the run of segments across a pod as a break, the catalog winning where it hears something, and coming back labels the programme as a returned skip does")]
    public void SeeksLabelWhatTheyPassed()
    {
        SegmentPiece[] pieces = [Piece(520, 600), Piece(600, 645), Piece(645, 660), Piece(660, 675), Piece(675, 688), Piece(688, 1800)];
        JoinedPiece pod = new(1, 3, Piece(600, 675), [SegmentKind.Break]);
        ViewerSeeks seeks = ViewerSeeks.Read([Forward(SeekMeans.Bar, 595, 681)], AroundThePod, null, Tolerance);
        SegmentContext heard = Context([new CatalogHeard(Seconds(600), Seconds(645), SegmentKind.Break, 3, 2, 2, 0.6)]);

        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(pieces, heard, [], seeks: seeks);
        IReadOnlyList<SegmentLabel?> joined = SegmentLabels.OfJoined([pod], Context(), [], seeks);

        Assert.Equal(
            [null, new SegmentLabel(SegmentKind.Break, 0.6, SegmentLabelSource.Catalog), Viewer(SegmentKind.Break, 0.1), Viewer(SegmentKind.Break, 0.1), null, null],
            labels);
        Assert.Equal([Viewer(SegmentKind.Break, 0.1)], joined);
    }

    [Fact(DisplayName = "BR-CV-004: coming back into what a seek passed labels ten seconds from there as the programme at two tenths")]
    public void ComingBackLabelsTheProgramme()
    {
        SegmentPiece[] pieces = [Piece(520, 600), Piece(600, 612), Piece(612, 675), Piece(675, 1800)];
        ViewerSeeks seeks = ViewerSeeks.Read(
            [Forward(SeekMeans.Bar, 595, 681), new ViewerSeek(Evening.AddSeconds(20), SeekAction.Returned, SeekMeans.Bar, Seconds(690), Seconds(601))],
            AroundThePod,
            null,
            Tolerance);

        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(pieces, Context(), [], seeks: seeks);

        Assert.Equal([null, Viewer(SegmentKind.Programme, 0.2), null, null], labels);
    }

    private static ViewerSeek Forward(SeekMeans means, double from, double to) => new(Evening, SeekAction.Forward, means, Seconds(from), Seconds(to));

    private static BoundaryCandidate Cut(double at, BoundaryCue cues) => new(Seconds(at), Seconds(at), Seconds(at), cues, 0, TimeSpan.Zero);

    private static SegmentSpan Span(double from, double to, SegmentKind kind) => new(Seconds(from), Seconds(to), kind, null);

    private static SegmentLabel Viewer(SegmentKind kind, double weight) => new(kind, weight, SegmentLabelSource.Viewer);

    private static SegmentPiece Piece(double from, double to)
        => new(Seconds(from), Seconds(to), Seconds(to - from), SegmentFeatureValues.Of(new Dictionary<string, double?> { [SegmentFeature.OutsideGuide] = 0 }));

    private static SegmentContext Context(IReadOnlyList<CatalogHeard>? catalog = null)
        => new(new NetworkId(1), new ServiceId(1), "架空の物語", null, null, [], catalog ?? [], CornerLogo.None, []);

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);
}
