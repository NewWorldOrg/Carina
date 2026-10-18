using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentMetricsTests
{
    private const double Length = 1800;

    private static readonly DateTime Evening = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private static readonly SegmentSpan[] Answer =
    [
        Span(0, 600, SegmentKind.Programme),
        Span(600, 690, SegmentKind.Break),
        Span(690, 1200, SegmentKind.Programme),
        Span(1200, 1230, SegmentKind.Break),
        Span(1230, Length, SegmentKind.Programme),
    ];

    [Fact(DisplayName = "boundaries count as found when a detected one turns the same way within two seconds or one, and a detected one with no answered boundary near is extra")]
    public void BoundariesAreFoundByDirectionAndDistance()
    {
        SegmentDetection detection = Detected(
            Span(0, 601.5, SegmentKind.Programme, 0.9),
            Span(601.5, 690.5, SegmentKind.Break, 0.9),
            Span(690.5, 1000, SegmentKind.Programme, 0.9),
            Span(1000, 1015, SegmentKind.Break, 0.9),
            Span(1015, Length, SegmentKind.Programme, 0.9));

        SegmentMetrics metrics = SegmentMetrics.Of([Compared(Answer, detection)], 0.7, SkipReturns.None)!;

        Assert.Equal(0.5, metrics.BoundaryRecallWithin2s);
        Assert.Equal(0.25, metrics.BoundaryRecallWithin1s);
        Assert.Equal(2, metrics.ExtraBoundariesPerRecording);
        Assert.Equal(15.5 * 2, metrics.ProgrammeSkippedSecondsPerHour, 9);
        Assert.Equal([new KindMetrics(SegmentKind.Break, 88.5 / 120, 88.5 / 104), Unmeasured(SegmentKind.Opening), Unmeasured(SegmentKind.Ending)], metrics.Kinds);
        Assert.Null(metrics.ReturnedAfterSkipShare);
        Assert.Equal(0, metrics.Skips);
    }

    [Fact(DisplayName = "a detected boundary at the right moment turning the other way is not found, and programme is skippable only from the threshold")]
    public void TheWayABoundaryTurnsAndTheThresholdCount()
    {
        SegmentDetection detection = Detected(
            Span(0, 540, SegmentKind.Programme, 0.9),
            Span(540, 600, SegmentKind.Break, 0.6),
            Span(600, 690, SegmentKind.Programme, 0.9),
            Span(690, 720, SegmentKind.Break, 0.8),
            Span(720, Length, SegmentKind.Programme, 0.9));

        SegmentMetrics low = SegmentMetrics.Of([Compared(Answer, detection)], 0.5, SkipReturns.None)!;
        SegmentMetrics high = SegmentMetrics.Of([Compared(Answer, detection)], 0.7, SkipReturns.None)!;

        Assert.Equal(0, low.BoundaryRecallWithin2s);
        Assert.Equal(2, low.ExtraBoundariesPerRecording);
        Assert.Equal(90 * 2, low.ProgrammeSkippedSecondsPerHour, 9);
        Assert.Equal(30 * 2, high.ProgrammeSkippedSecondsPerHour, 9);
    }

    [Fact(DisplayName = "a detection thrown away has no boundaries and offers no skip, and seconds of a kind never detected have no precision")]
    public void ADiscardedDetectionShowsNothing()
    {
        SegmentDetection discarded = Detected(Span(0, 600, SegmentKind.Programme, 0.9), Span(600, Length, SegmentKind.Break, 0.9));

        SegmentMetrics metrics = SegmentMetrics.Of([Compared(Answer, discarded)], 0.7, SkipReturns.None)!;

        Assert.Equal(SegmentDetectionState.Discarded, discarded.State);
        Assert.Equal(0, metrics.BoundaryRecallWithin2s);
        Assert.Equal(0, metrics.ExtraBoundariesPerRecording);
        Assert.Equal(0, metrics.ProgrammeSkippedSecondsPerHour);
        Assert.Equal([new KindMetrics(SegmentKind.Break, 0, null), Unmeasured(SegmentKind.Opening), Unmeasured(SegmentKind.Ending)], metrics.Kinds);
    }

    [Fact(DisplayName = "over several recordings the extra boundaries are per recording and the skipped programme per hour of them all, and answers without a boundary leave the recall unknown")]
    public void SeveralRecordingsAreAveragedOverTheirCountAndLength()
    {
        SegmentSpan[] programmeOnly = [Span(0, Length, SegmentKind.Programme)];
        SegmentDetection falseBreak = Detected(Span(0, 900, SegmentKind.Programme, 0.9), Span(900, 930, SegmentKind.Break, 0.9), Span(930, Length, SegmentKind.Programme, 0.9));
        SegmentDetection nothing = Detected(Span(0, Length, SegmentKind.Programme, 0.9));

        SegmentMetrics metrics = SegmentMetrics.Of([Compared(programmeOnly, falseBreak), Compared(programmeOnly, nothing)], 0.7, new SkipReturns(25, 3))!;

        Assert.Null(metrics.BoundaryRecallWithin2s);
        Assert.Equal(1, metrics.ExtraBoundariesPerRecording);
        Assert.Equal(30, metrics.ProgrammeSkippedSecondsPerHour, 9);
        Assert.Equal([new KindMetrics(SegmentKind.Break, null, 0), Unmeasured(SegmentKind.Opening), Unmeasured(SegmentKind.Ending)], metrics.Kinds);
        Assert.Equal(0.12, metrics.ReturnedAfterSkipShare);
        Assert.Equal(25, metrics.Skips);
        Assert.Null(SegmentMetrics.Of([], 0.7, SkipReturns.None));
    }

    [Fact(DisplayName = "BR-CD-008: the seconds of openings and endings found are measured as those of breaks, the boundaries into and out of them count, and a button over either that skips programme counts against it")]
    public void OpeningsAndEndingsAreMeasured()
    {
        SegmentSpan[] answer =
        [
            Span(0, 60, SegmentKind.Programme),
            Span(60, 150, SegmentKind.Opening),
            Span(150, 900, SegmentKind.Programme),
            Span(900, 960, SegmentKind.Break),
            Span(960, 1650, SegmentKind.Programme),
            Span(1650, 1740, SegmentKind.Ending),
            Span(1740, Length, SegmentKind.Programme),
        ];
        SegmentDetection detection = Detected(
            Span(0, 61, SegmentKind.Programme, 0.9),
            Span(61, 160, SegmentKind.Opening, 0.9),
            Span(160, 900, SegmentKind.Programme, 0.9),
            Span(900, 960, SegmentKind.Break, 0.9),
            Span(960, Length, SegmentKind.Programme, 0.9));

        SegmentMetrics metrics = SegmentMetrics.Of([Compared(answer, detection)], 0.7, SkipReturns.None)!;

        Assert.Equal(
            [new KindMetrics(SegmentKind.Break, 1, 1), new KindMetrics(SegmentKind.Opening, 89 / 90.0, 89 / 99.0), new KindMetrics(SegmentKind.Ending, 0, null)],
            metrics.Kinds);
        Assert.Equal(3 / 6.0, metrics.BoundaryRecallWithin2s);
        Assert.Equal(3 / 6.0, metrics.BoundaryRecallWithin1s);
        Assert.Equal(1, metrics.ExtraBoundariesPerRecording);
        Assert.Equal(10 * 2, metrics.ProgrammeSkippedSecondsPerHour, 9);
    }

    [Fact(DisplayName = "A-051: a first episode with no opening, answered and detected as such, counts the opening neither as found nor as missed, and one detected where none was answered lowers its precision alone")]
    public void AFirstEpisodeWithNoOpeningIsNoMiss()
    {
        SegmentSpan[] withOpening = [Span(0, 60, SegmentKind.Programme), Span(60, 150, SegmentKind.Opening), Span(150, Length, SegmentKind.Programme)];
        SegmentSpan[] withoutOpening = [Span(0, Length, SegmentKind.Programme)];
        SegmentDetection found = Detected(Span(0, 60, SegmentKind.Programme, 0.9), Span(60, 150, SegmentKind.Opening, 0.9), Span(150, Length, SegmentKind.Programme, 0.9));
        SegmentDetection none = Detected(Span(0, Length, SegmentKind.Programme, 0.9));

        SegmentMetrics alone = SegmentMetrics.Of([Compared(withOpening, found)], 0.7, SkipReturns.None)!;
        SegmentMetrics beside = SegmentMetrics.Of([Compared(withOpening, found), Compared(withoutOpening, none)], 0.7, SkipReturns.None)!;
        SegmentMetrics wrong = SegmentMetrics.Of([Compared(withOpening, found), Compared(withoutOpening, found)], 0.7, SkipReturns.None)!;

        Assert.Equal(new KindMetrics(SegmentKind.Opening, 1, 1), alone.Kinds[1]);
        Assert.Equal(new KindMetrics(SegmentKind.Opening, 1, 1), beside.Kinds[1]);
        Assert.Equal(alone.BoundaryRecallWithin2s, beside.BoundaryRecallWithin2s);
        Assert.Equal(0, beside.ExtraBoundariesPerRecording);
        Assert.Equal(new KindMetrics(SegmentKind.Opening, 1, 0.5), wrong.Kinds[1]);
    }

    [Fact(DisplayName = "the recommended threshold is the lowest hundredth at which no more than five seconds of programme an hour are skippable")]
    public void TheRecommendedThresholdIsTheLowestThatHolds()
    {
        SegmentDetection detection = Detected(
            Span(0, 600, SegmentKind.Programme, 0.9),
            Span(600, 690.5, SegmentKind.Break, 0.9),
            Span(690.5, 1000, SegmentKind.Programme, 0.9),
            Span(1000, 1015, SegmentKind.Break, 0.82),
            Span(1015, 1300, SegmentKind.Programme, 0.9),
            Span(1300, 1330, SegmentKind.Break, 0.6),
            Span(1330, Length, SegmentKind.Programme, 0.9));
        SegmentDetection certain = Detected(Span(0, 900, SegmentKind.Programme, 0.9), Span(900, 930, SegmentKind.Break, 0.99), Span(930, Length, SegmentKind.Programme, 0.9));

        Assert.Equal(0.83, SegmentMetrics.RecommendedThreshold([Compared(Answer, detection)]));
        Assert.Equal(0.5, SegmentMetrics.RecommendedThreshold([Compared(Answer, Detected(Answer))]));
        Assert.Null(SegmentMetrics.RecommendedThreshold([Compared(Answer, certain)]));
        Assert.Null(SegmentMetrics.RecommendedThreshold([]));
    }

    [Theory(DisplayName = "an evaluation stands on enough with ten confirmed recordings and a hundred boundaries of breaks, and not with one fewer of either")]
    [InlineData(10, 25, true)]
    [InlineData(9, 30, false)]
    [InlineData(12, 4, false)]
    public void EnoughEvidenceNeedsTenRecordingsAndAHundredBoundaries(int recordings, int pods, bool enough)
    {
        SegmentSpan[] answer =
        [
            .. Enumerable.Range(0, pods).SelectMany(pod => new[] { Span(pod * 60, (pod * 60) + 45, SegmentKind.Programme), Span((pod * 60) + 45, (pod * 60) + 60, SegmentKind.Break) }),
            Span(pods * 60, (pods * 60) + 60, SegmentKind.Programme),
        ];
        SegmentComparison[] compared = [.. Enumerable.Range(0, recordings).Select(_ => Compared(answer, Detected(Span(0, (pods * 60) + 60, SegmentKind.Programme, 0.9))))];

        EvaluationEvidence evidence = EvaluationEvidence.Of(compared, Evening);

        Assert.Equal(recordings, evidence.ConfirmedRecordings);
        Assert.Equal(recordings * pods * 2, evidence.Boundaries);
        Assert.Equal(Evening, evidence.LabelsUntil);
        Assert.Equal(enough, evidence.Enough);
        Assert.False(new EvaluationEvidence(10, 99, null).Enough);
        Assert.True(new EvaluationEvidence(10, 100, null).Enough);
    }

    [Fact(DisplayName = "BR-CD-008: the answers give an evaluation the confirmed ones that stand, the boundaries of breaks they draw, and the latest of them sent")]
    public void TheAnswersGiveTheConfirmedOnesThatStand()
    {
        SegmentSpan[] twoBreaks = [Span(0, 300, SegmentKind.Programme), Span(300, 360, SegmentKind.Break), Span(360, 900, SegmentKind.Programme), Span(900, 960, SegmentKind.Break), Span(960, 1800, SegmentKind.Programme)];
        SegmentSpan[] oneBreak = [Span(0, 300, SegmentKind.Programme), Span(300, 360, SegmentKind.Break), Span(360, 1800, SegmentKind.Programme)];
        RecordingAnswer[] answers =
        [
            Answered(twoBreaks, confirmed: true, Evening),
            Answered(oneBreak, confirmed: true, Evening.AddHours(2)),
            Answered(twoBreaks, confirmed: false, Evening.AddHours(3)),
            Answered(twoBreaks, confirmed: true, Evening.AddHours(4), withdrawnAt: Evening.AddHours(5)),
        ];

        EvaluationEvidence evidence = EvaluationEvidence.OfAnswers(answers);

        Assert.Equal(new EvaluationEvidence(2, 6, Evening.AddHours(2)), evidence);
        Assert.Equal(new EvaluationEvidence(0, 0, null), EvaluationEvidence.OfAnswers([]));
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
        SegmentMetrics evaluated = new(0.93, 0.85, 1.2, 2.1, [new KindMetrics(SegmentKind.Break, 0.95, 0.97)], 0.5, 4);

        SegmentMetrics lately = evaluated.WithReturns(new SkipReturns(64, 2));
        SegmentMetrics none = evaluated.WithReturns(SkipReturns.None);

        Assert.Equal((0.93, 0.85, 1.2, 2.1), (lately.BoundaryRecallWithin2s!.Value, lately.BoundaryRecallWithin1s!.Value, lately.ExtraBoundariesPerRecording, lately.ProgrammeSkippedSecondsPerHour));
        Assert.Equal(evaluated.Kinds, lately.Kinds);
        Assert.Equal((2 / 64d, 64), (lately.ReturnedAfterSkipShare!.Value, lately.Skips));
        Assert.Equal(((double?)null, 0), (none.ReturnedAfterSkipShare, none.Skips));
    }

    private static RecordingAnswer Answered(SegmentSpan[] segments, bool confirmed, DateTime at, DateTime? withdrawnAt = null)
        => RecordingAnswer.Rehydrate(RecordingId.New(), segments, segments[^1].Ends, confirmed, confirmed ? at : null, at, 1, withdrawnAt);

    private static ViewerAction Action(ViewerActionKind action, int days, int? version)
        => new(Evening.AddDays(days), action, SegmentKind.Break, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690), version);

    private static SegmentComparison Compared(IReadOnlyList<SegmentSpan> answer, SegmentDetection detection)
        => new(new NetworkId(1), new ServiceId(1), answer, detection);

    private static SegmentDetection Detected(params SegmentSpan[] spans) => SegmentDetection.Judged(spans, spans[^1].Ends, []);

    private static SegmentSpan Span(double from, double to, SegmentKind kind, double? probability = null)
        => new(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to), kind, probability);

    private static KindMetrics Unmeasured(SegmentKind kind) => new(kind, null, null);
}
