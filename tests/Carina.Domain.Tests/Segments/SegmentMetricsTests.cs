using Carina.Domain.Channels;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentMetricsTests
{
    private const double Length = 1800;

    private static readonly DateTime Evening = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CatalogTruth TwoAdverts = Truth((600, 690, SegmentKind.Break), (1200, 1230, SegmentKind.Break));

    [Fact(DisplayName = "BR-CD-008: a boundary the catalog is sure of is found when a detected one turns into its kind where it starts, or out of it where it ends, within two seconds or one, and the seconds of adverts are those detected as breaks")]
    public void BoundariesAreFoundByDirectionAndDistance()
    {
        SegmentDetection detection = Detected(
            Span(0, 601.5, SegmentKind.Programme, 0.9),
            Span(601.5, 690.5, SegmentKind.Break, 0.9),
            Span(690.5, 1000, SegmentKind.Programme, 0.9),
            Span(1000, 1015, SegmentKind.Break, 0.9),
            Span(1015, Length, SegmentKind.Programme, 0.9));

        SegmentMetrics metrics = SegmentMetrics.Of([Compared(TwoAdverts, detection)], SkipReturns.None)!;

        Assert.Equal(0.5, metrics.BoundaryRecallWithin2s);
        Assert.Equal(0.25, metrics.BoundaryRecallWithin1s);
        Assert.Equal([new KindMetrics(SegmentKind.Break, 88.5 / 120), Unmeasured(SegmentKind.Opening), Unmeasured(SegmentKind.Ending)], metrics.Kinds);
        Assert.Equal(88.5 / 120, metrics.BreakRecall);
        Assert.Null(metrics.ReturnedAfterSkipShare);
        Assert.Equal(0, metrics.Skips);
    }

    [Fact(DisplayName = "BR-CD-008: a detected boundary at the right moment turning the wrong way is not found, and one turning into the advert from any other kind is")]
    public void TheWayABoundaryTurnsCounts()
    {
        CatalogTruth advert = Truth((600, 690, SegmentKind.Break));
        SegmentDetection wrongWay = Detected(
            Span(0, 540, SegmentKind.Programme, 0.9),
            Span(540, 600, SegmentKind.Break, 0.6),
            Span(600, 690, SegmentKind.Programme, 0.9),
            Span(690, 720, SegmentKind.Break, 0.8),
            Span(720, Length, SegmentKind.Programme, 0.9));
        SegmentDetection afterAnEnding = Detected(
            Span(0, 510, SegmentKind.Programme, 0.9),
            Span(510, 600, SegmentKind.Ending, 0.9),
            Span(600, 690, SegmentKind.Break, 0.9),
            Span(690, Length, SegmentKind.Programme, 0.9));

        Assert.Equal(0, SegmentMetrics.Of([Compared(advert, wrongWay)], SkipReturns.None)!.BoundaryRecallWithin2s);
        Assert.Equal(1, SegmentMetrics.Of([Compared(advert, afterAnEnding)], SkipReturns.None)!.BoundaryRecallWithin2s);
    }

    [Fact(DisplayName = "BR-CD-008: a detection thrown away has no boundaries and finds no second")]
    public void ADiscardedDetectionShowsNothing()
    {
        SegmentDetection discarded = Detected(Span(0, 600, SegmentKind.Programme, 0.9), Span(600, Length, SegmentKind.Break, 0.9));

        SegmentMetrics metrics = SegmentMetrics.Of([Compared(TwoAdverts, discarded)], SkipReturns.None)!;

        Assert.Equal(SegmentDetectionState.Discarded, discarded.State);
        Assert.Equal(0, metrics.BoundaryRecallWithin2s);
        Assert.Equal([new KindMetrics(SegmentKind.Break, 0), Unmeasured(SegmentKind.Opening), Unmeasured(SegmentKind.Ending)], metrics.Kinds);
    }

    [Fact(DisplayName = "BR-CD-008: what a detection finds beyond what the catalog is sure of is not counted against it")]
    public void WhatTheCatalogIsNotSureOfCountsForNothing()
    {
        SegmentDetection detection = Detected(
            Span(0, 600, SegmentKind.Programme, 0.9),
            Span(600, 690, SegmentKind.Break, 0.9),
            Span(690, 1000, SegmentKind.Programme, 0.9),
            Span(1000, 1015, SegmentKind.Break, 0.9),
            Span(1015, 1200, SegmentKind.Programme, 0.9),
            Span(1200, 1230, SegmentKind.Break, 0.9),
            Span(1230, Length, SegmentKind.Programme, 0.9));

        SegmentMetrics metrics = SegmentMetrics.Of([Compared(TwoAdverts, detection)], SkipReturns.None)!;

        Assert.Equal((1.0, 1.0, 1.0), (metrics.BoundaryRecallWithin2s!.Value, metrics.BoundaryRecallWithin1s!.Value, metrics.BreakRecall!.Value));
    }

    [Fact(DisplayName = "BR-CD-008: the seconds of openings and endings the catalog is sure of are measured as those of adverts, and the boundaries into and out of them count")]
    public void OpeningsAndEndingsAreMeasured()
    {
        CatalogTruth truth = Truth((60, 150, SegmentKind.Opening), (900, 960, SegmentKind.Break), (1650, 1740, SegmentKind.Ending));
        SegmentDetection detection = Detected(
            Span(0, 61, SegmentKind.Programme, 0.9),
            Span(61, 160, SegmentKind.Opening, 0.9),
            Span(160, 900, SegmentKind.Programme, 0.9),
            Span(900, 960, SegmentKind.Break, 0.9),
            Span(960, Length, SegmentKind.Programme, 0.9));

        SegmentMetrics metrics = SegmentMetrics.Of([Compared(truth, detection)], SkipReturns.None)!;

        Assert.Equal([new KindMetrics(SegmentKind.Break, 1), new KindMetrics(SegmentKind.Opening, 89 / 90.0), new KindMetrics(SegmentKind.Ending, 0)], metrics.Kinds);
        Assert.Equal(3 / 6.0, metrics.BoundaryRecallWithin2s);
        Assert.Equal(3 / 6.0, metrics.BoundaryRecallWithin1s);
    }

    [Fact(DisplayName = "BR-CD-008: over several recordings the figures are summed, a kind the catalog is sure of nowhere is not known, and with no recording there are no figures")]
    public void SeveralRecordingsAreSummed()
    {
        CatalogTruth opening = Truth((60, 150, SegmentKind.Opening));
        SegmentDetection nothing = Detected(Span(0, Length, SegmentKind.Programme, 0.9));
        SegmentDetection exact = Detected(Span(0, 600, SegmentKind.Programme, 0.9), Span(600, 690, SegmentKind.Break, 0.9), Span(690, Length, SegmentKind.Programme, 0.9));

        SegmentMetrics metrics = SegmentMetrics.Of([Compared(Truth((600, 690, SegmentKind.Break)), exact), Compared(opening, nothing)], new SkipReturns(25, 3))!;

        Assert.Equal(2 / 4.0, metrics.BoundaryRecallWithin2s);
        Assert.Equal([new KindMetrics(SegmentKind.Break, 1), new KindMetrics(SegmentKind.Opening, 0), Unmeasured(SegmentKind.Ending)], metrics.Kinds);
        Assert.Equal(0.12, metrics.ReturnedAfterSkipShare);
        Assert.Equal(25, metrics.Skips);
        Assert.Null(SegmentMetrics.Of([], SkipReturns.None));
    }

    [Fact(DisplayName = "BR-CD-008: the catalog is sure of adverts heard in two series or on two stations and of openings and endings, adverts less than two seconds apart are one pod, and no boundary is drawn within two seconds of the recording's zero or end")]
    public void WhatTheCatalogIsSureOf()
    {
        CatalogTruth truth = CatalogTruth.Of(
            [
                Heard(0.5, 15, SegmentKind.Break, 0.9),
                Heard(15.8, 30, SegmentKind.Break, 0.9),
                Heard(60, 150, SegmentKind.Opening, 0.8),
                Heard(300, 315, SegmentKind.Break, 0.5),
                Heard(330, 340, SegmentKind.Break, 0.6),
                Heard(400, 415, SegmentKind.Break, 0.8),
                Heard(500, 590, SegmentKind.Programme, 0.8),
                Heard(600, 615, SegmentKind.Break, 0.9),
                Heard(618, 633, SegmentKind.Break, 0.9),
                Heard(700, 715, null, null),
                Heard(1790, 1799.5, SegmentKind.Break, 0.9),
            ],
            TimeSpan.FromSeconds(Length), []);

        Assert.Equal(
            [
                Span(0.5, 30, SegmentKind.Break),
                Span(60, 150, SegmentKind.Opening),
                Span(600, 615, SegmentKind.Break),
                Span(618, 633, SegmentKind.Break),
                Span(1790, 1799.5, SegmentKind.Break),
            ],
            truth.Stretches);
        Assert.Equal(
            [
                Boundary(30, SegmentKind.Break, false),
                Boundary(60, SegmentKind.Opening, true),
                Boundary(150, SegmentKind.Opening, false),
                Boundary(600, SegmentKind.Break, true),
                Boundary(615, SegmentKind.Break, false),
                Boundary(618, SegmentKind.Break, true),
                Boundary(633, SegmentKind.Break, false),
                Boundary(1790, SegmentKind.Break, true),
            ],
            truth.Boundaries);
        Assert.Equal((true, true, 6), (truth.Judges, truth.HoldsBreaks, truth.BreakBoundaries));
        Assert.False(CatalogTruth.Of([Heard(300, 315, SegmentKind.Break, 0.5)], TimeSpan.FromSeconds(Length), []).Judges);
        Assert.Throws<ArgumentException>(() => Compared(CatalogTruth.None, Detected(Span(0, Length, SegmentKind.Programme, 0.9))));
    }

    [Fact(DisplayName = "BR-CD-008: adverts heard less than two seconds apart are one pod when the catalog is sure of one of them, whatever it heard the others as, and its boundaries are the pod's outer edges, not the joins inside it; a run it is sure of none of is left out")]
    public void APodIsOneRunWithItsOuterEdges()
    {
        CatalogTruth truth = CatalogTruth.Of(
            [
                Heard(600, 615, SegmentKind.Break, 0.9),
                Heard(615.8, 630, SegmentKind.Break, 0.5),
                Heard(630.8, 645, SegmentKind.Break, 0.9),
                Heard(900, 915, SegmentKind.Break, 0.5),
                Heard(915.8, 930, SegmentKind.Break, 0.6),
                Heard(1200, 1265, SegmentKind.Ending, 0.8),
                Heard(1266, 1290, SegmentKind.Ending, 0.8),
            ],
            TimeSpan.FromSeconds(Length), []);

        Assert.Equal([Span(600, 645, SegmentKind.Break), Span(1200, 1290, SegmentKind.Ending)], truth.Stretches);
        Assert.Equal(
            [Boundary(600, SegmentKind.Break, true), Boundary(645, SegmentKind.Break, false), Boundary(1200, SegmentKind.Ending, true), Boundary(1290, SegmentKind.Ending, false)],
            truth.Boundaries);
        Assert.Equal(2, truth.BreakBoundaries);
        Assert.Equal(new EvaluationEvidence(1, 2, null), EvaluationEvidence.Of([truth]));
    }

    [Fact(DisplayName = "BR-CD-008: a detection that takes a pod whole finds both of its boundaries, one that cuts it at the joins between its adverts finds no more for them, and one that finds only the joins finds none")]
    public void CuttingAPodFindsNoMoreThanTakingItWhole()
    {
        CatalogTruth pod = CatalogTruth.Of(
            [Heard(600, 615, SegmentKind.Break, 0.9), Heard(615.8, 630, SegmentKind.Break, 0.5), Heard(630.8, 645, SegmentKind.Break, 0.9)],
            TimeSpan.FromSeconds(Length), []);
        SegmentDetection whole = Detected(Span(0, 600, SegmentKind.Programme), Span(600, 645, SegmentKind.Break), Span(645, Length, SegmentKind.Programme));
        SegmentDetection cut = Detected(
            Span(0, 600, SegmentKind.Programme),
            Span(600, 615, SegmentKind.Break),
            Span(615, 630.8, SegmentKind.Programme),
            Span(630.8, 645, SegmentKind.Break),
            Span(645, Length, SegmentKind.Programme));
        SegmentDetection joins = Detected(Span(0, 615.8, SegmentKind.Programme), Span(615.8, 630, SegmentKind.Break), Span(630, Length, SegmentKind.Programme));

        Assert.Equal(1.0, SegmentMetrics.Of([Compared(pod, whole)], SkipReturns.None)!.BoundaryRecallWithin2s);
        Assert.Equal(1.0, SegmentMetrics.Of([Compared(pod, cut)], SkipReturns.None)!.BoundaryRecallWithin2s);
        Assert.Equal(0.0, SegmentMetrics.Of([Compared(pod, joins)], SkipReturns.None)!.BoundaryRecallWithin2s);
        Assert.Equal(1.0, SegmentMetrics.Of([Compared(pod, whole)], SkipReturns.None)!.BreakRecall);
    }

    [Fact(DisplayName = "BR-CD-008: an edge of a run with an advert's length outside it to the next silent cut is no boundary, since an advert the catalog has not heard twice may lie there, and one with the programme outside it is; a silence that is no cut is no seam, and openings and endings are treated alike")]
    public void AnEdgeBesideAnAdvertsLengthIsNoBoundary()
    {
        CatalogTruth truth = CatalogTruth.Of(
            [
                Heard(50, 140, SegmentKind.Opening, 0.8),
                Heard(600, 615, SegmentKind.Break, 0.9),
                Heard(615.5, 630, SegmentKind.Break, 0.9),
                Heard(1200, 1215, SegmentKind.Break, 0.9),
                Heard(1215.5, 1230, SegmentKind.Break, 0.9),
                Heard(1500, 1590, SegmentKind.Ending, 0.8),
            ],
            TimeSpan.FromSeconds(Length),
            [
                Ends(0),
                .. Cuts(140.4, 155.5, 548, 585, 600.2, 615.3, 630.3, 660.5, 700, 1150, 1200, 1215.3, 1230.2, 1283, 1590.2, 1640),
                Silence(1245.2),
                Ends(Length),
            ]);

        Assert.Equal(
            [
                Boundary(50, SegmentKind.Opening, true),
                Boundary(1200, SegmentKind.Break, true),
                Boundary(1230, SegmentKind.Break, false),
                Boundary(1500, SegmentKind.Ending, true),
                Boundary(1590, SegmentKind.Ending, false),
            ],
            truth.Boundaries);
        Assert.Equal(2, truth.BreakBoundaries);
        Assert.Equal(new EvaluationEvidence(1, 2, null), EvaluationEvidence.Of([truth]));
    }

    [Fact(DisplayName = "BR-CD-008: a detection that takes a whole pod, unheard adverts of fifteen and thirty seconds on either side of what the catalog is sure of included, finds every boundary, and one that cuts the pod where the catalog stops hearing finds no more")]
    public void TakingAPodWithUnheardAdvertsWholeIsRight()
    {
        SegmentEvidence evidence = new SyntheticReadings(Length)
            .QuietAround(548)
            .QuietAround(585)
            .QuietAround(600)
            .QuietAround(615)
            .QuietAround(630)
            .QuietAround(660)
            .QuietAround(1137)
            .QuietAround(1200)
            .QuietAround(1215)
            .QuietAround(1230)
            .QuietAround(1283)
            .Evidence();
        CatalogTruth truth = CatalogTruth.Of(
            [
                Heard(600.3, 614.7, SegmentKind.Break, 0.9),
                Heard(615.3, 629.7, SegmentKind.Break, 0.9),
                Heard(1200.3, 1214.7, SegmentKind.Break, 0.9),
                Heard(1215.3, 1229.7, SegmentKind.Break, 0.9),
            ],
            evidence);
        SegmentDetection whole = Detected(
            Span(0, 585, SegmentKind.Programme),
            Span(585, 660, SegmentKind.Break),
            Span(660, 1200, SegmentKind.Programme),
            Span(1200, 1230, SegmentKind.Break),
            Span(1230, Length, SegmentKind.Programme));
        SegmentDetection cut = Detected(
            Span(0, 600, SegmentKind.Programme),
            Span(600, 630, SegmentKind.Break),
            Span(630, 1200, SegmentKind.Programme),
            Span(1200, 1230, SegmentKind.Break),
            Span(1230, Length, SegmentKind.Programme));

        Assert.Equal([Boundary(1200.3, SegmentKind.Break, true), Boundary(1229.7, SegmentKind.Break, false)], truth.Boundaries);
        Assert.Equal(1.0, SegmentMetrics.Of([Compared(truth, whole)], SkipReturns.None)!.BoundaryRecallWithin2s);
        Assert.Equal(1.0, SegmentMetrics.Of([Compared(truth, cut)], SkipReturns.None)!.BoundaryRecallWithin2s);
        Assert.Equal(1.0, SegmentMetrics.Of([Compared(truth, whole)], SkipReturns.None)!.BreakRecall);
    }

    [Theory(DisplayName = "BR-CD-008: an evaluation stands on enough with ten recordings the catalog is sure of an advert in and thirty boundaries of the pods of those adverts, and not with one fewer of either")]
    [InlineData(10, 2, true)]
    [InlineData(9, 4, false)]
    [InlineData(14, 1, false)]
    public void EnoughEvidenceNeedsTenRecordingsAndThirtyBoundaries(int recordings, int adverts, bool enough)
    {
        CatalogTruth truth = Truth([.. Enumerable.Range(0, adverts).Select(advert => ((advert * 60) + 45.0, (advert * 60) + 60.0, SegmentKind.Break))]);
        CatalogTruth openingOnly = Truth((60, 150, SegmentKind.Opening));
        SegmentComparison[] compared =
        [
            .. Enumerable.Range(0, recordings).Select(_ => Compared(truth, Detected(Span(0, Length, SegmentKind.Programme, 0.9)))),
            Compared(openingOnly, Detected(Span(0, Length, SegmentKind.Programme, 0.9))),
        ];

        EvaluationEvidence evidence = EvaluationEvidence.Of(compared, Evening);

        Assert.Equal(recordings, evidence.Recordings);
        Assert.Equal(recordings * adverts * 2, evidence.Boundaries);
        Assert.Equal(Evening, evidence.LabelsUntil);
        Assert.Equal(enough, evidence.Enough);
        Assert.Equal(new EvaluationEvidence(recordings, recordings * adverts * 2, null), EvaluationEvidence.Of([.. compared.Select(comparison => comparison.Truth)]));
        Assert.False(new EvaluationEvidence(9, 30, null).Enough);
        Assert.False(new EvaluationEvidence(10, 29, null).Enough);
        Assert.True(new EvaluationEvidence(10, 30, null).Enough);
    }

    [Fact(DisplayName = "skips and returns are counted on one version's buttons within the days asked for")]
    public void SkipsAndReturnsAreCountedForOneVersion()
    {
        ViewerAction[] actions =
        [
            Action(ViewerActionKind.Skipped, 0, 3),
            Action(ViewerActionKind.Skipped, 1, 3),
            Action(ViewerActionKind.Returned, 1, 3),
            Action(ViewerActionKind.Dismissed, 2, 3),
            Action(ViewerActionKind.Skipped, 2, 2),
            Action(ViewerActionKind.Skipped, 8, 3),
            Action(ViewerActionKind.Skipped, 2, null),
        ];

        SkipReturns returns = SkipReturns.Of(actions, 3, Evening, Evening.AddDays(7));

        Assert.Equal((2, 1, 0.5), (returns.Skips, returns.Returned, returns.Share!.Value));
        Assert.Null(SkipReturns.None.Share);
    }

    [Fact(DisplayName = "BR-CD-008: the figures read with what viewers did lately keep every other figure and take the share that came back and the skips from it")]
    public void TheFiguresReadWithWhatViewersDidLatelyKeepTheRest()
    {
        SegmentMetrics evaluated = new(0.93, 0.85, [new KindMetrics(SegmentKind.Break, 0.95)], 0.5, 4);

        SegmentMetrics lately = evaluated.WithReturns(new SkipReturns(64, 2));
        SegmentMetrics none = evaluated.WithReturns(SkipReturns.None);

        Assert.Equal((0.93, 0.85), (lately.BoundaryRecallWithin2s!.Value, lately.BoundaryRecallWithin1s!.Value));
        Assert.Equal(evaluated.Kinds, lately.Kinds);
        Assert.Equal((2 / 64d, 64), (lately.ReturnedAfterSkipShare!.Value, lately.Skips));
        Assert.Equal(((double?)null, 0), (none.ReturnedAfterSkipShare, none.Skips));
    }

    private static CatalogTruth Truth(params (double From, double To, SegmentKind Kind)[] stretches)
        => CatalogTruth.Of([.. stretches.Select(stretch => Heard(stretch.From, stretch.To, stretch.Kind, stretch.Kind is SegmentKind.Break ? 0.9 : 0.8))], TimeSpan.FromSeconds(Length), []);

    private static CatalogHeard Heard(double from, double to, SegmentKind? kind, double? confidence)
        => new(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to), kind, 2, 2, 2, confidence, CatalogEntryId.New());

    private static TruthBoundary Boundary(double at, SegmentKind kind, bool enters) => new(TimeSpan.FromSeconds(at), kind, enters);

    private static IEnumerable<BoundaryCandidate> Cuts(params double[] seconds)
        => seconds.Select(at => Candidate(at, BoundaryCue.Silence | BoundaryCue.SilentCut));

    private static BoundaryCandidate Silence(double at) => Candidate(at, BoundaryCue.Silence);

    private static BoundaryCandidate Ends(double at) => Candidate(at, BoundaryCue.RecordingEdge);

    private static BoundaryCandidate Candidate(double at, BoundaryCue cues)
        => new(TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at), cues, 30, TimeSpan.FromSeconds(0.5));

    private static ViewerAction Action(ViewerActionKind action, int days, int? version)
        => new(Evening.AddDays(days), action, SegmentKind.Break, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690), version);

    private static SegmentComparison Compared(CatalogTruth truth, SegmentDetection detection) => new(new NetworkId(1), new ServiceId(1), truth, detection);

    private static SegmentDetection Detected(params SegmentSpan[] spans) => SegmentDetection.Judged(spans, spans[^1].Ends, []);

    private static SegmentSpan Span(double from, double to, SegmentKind kind, double? probability = null)
        => new(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to), kind, probability);

    private static KindMetrics Unmeasured(SegmentKind kind) => new(kind, null);
}
