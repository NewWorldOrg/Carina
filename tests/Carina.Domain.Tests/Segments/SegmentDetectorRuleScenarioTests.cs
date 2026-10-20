using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

/// <summary>
/// The scenarios of the rule-based detection's tests, each written as readings and run through the
/// versions of the model made by hand, of two kinds and of four, beside the rule-based detection itself, the
/// catalog weighing in neither. The version that tells the opening and the ending in any series hears speech
/// between the cues, since sound that keeps its loudness everywhere is a song. The scenarios that turn a setting
/// the model has no counterpart for (the grid's step, the share of breaks allowed and the number of marks
/// allowed) are left out, and so is the one that counts marks per recording rather than per hour.
/// </summary>
public sealed class SegmentDetectorRuleScenarioTests
{
    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    private static readonly SegmentContext Bare = new(new NetworkId(1), new ServiceId(1), null, null, null, [], [], CornerLogo.None, []);

    private static readonly Dictionary<string, Scenario> Listed = new(StringComparer.Ordinal)
    {
        ["nothing observed"] = new(new ChapterEvidence(), HalfAnHour, new ChapterSettings()),
        ["one boundary on its own"] = new(Observed(At(600)), HalfAnHour, new ChapterSettings()),
        ["quiet on its own every minute"] = new(new ChapterEvidence { Silences = [.. Enumerable.Range(1, 20).Select(step => Quiet(At(60 * step)))] }, HalfAnHour, new ChapterSettings()),
        ["two quiet stretches a grid apart, one corroborated"] = new(
            new ChapterEvidence { Silences = [Quiet(At(300)), Quiet(At(360))], Scenes = [new ChapterScene(At(300), 0.9)] },
            HalfAnHour,
            new ChapterSettings()),
        ["a pod of adverts"] = new(Observed(At(300), At(360)), HalfAnHour, new ChapterSettings()),
        ["two pods"] = new(Observed(At(300), At(360), At(900), At(1020)), HalfAnHour, new ChapterSettings()),
        ["a pair off the grid beyond the tolerance"] = new(Observed(At(300), At(362.5)), HalfAnHour, new ChapterSettings()),
        ["a pair just inside the tolerance"] = new(Observed(At(300), At(360.9)), HalfAnHour, new ChapterSettings()),
        ["a pair just outside the tolerance"] = new(Observed(At(300), At(361.1)), HalfAnHour, new ChapterSettings()),
        ["a pod off the grid pulled onto it"] = new(Observed(At(300), At(360.7)), HalfAnHour, new ChapterSettings()),
        ["two pods that overlap"] = new(Observed(At(300), At(330), At(360), At(420)), HalfAnHour, new ChapterSettings()),
        ["a sliver of programme between two pods"] = new(Observed(At(300), At(360), At(361.5), At(421.5)), HalfAnHour, new ChapterSettings()),
        ["a pod starting almost at the beginning"] = new(Observed(At(1.5), At(61.5)), HalfAnHour, new ChapterSettings()),
        ["breaks taking more than half the length"] = new(Pods(9, every: 190, lasting: 105), HalfAnHour, new ChapterSettings()),
        ["a pair 20 seconds apart"] = new(Observed(At(300), At(320)), HalfAnHour, new ChapterSettings()),
        ["a pair two seconds off the grid"] = new(Observed(At(300), At(362)), HalfAnHour, new ChapterSettings()),
        ["a pair two seconds off the grid with a tolerance of three"] = new(
            Observed(At(300), At(362)),
            HalfAnHour,
            new ChapterSettings { GridTolerance = TimeSpan.FromSeconds(3) }),
        ["changes of 0.20 at the quiet"] = new(Changed(0.20), HalfAnHour, new ChapterSettings()),
        ["changes of 0.20 at the quiet with a threshold of 0.10"] = new(Changed(0.20), HalfAnHour, new ChapterSettings { Scene = 0.10 }),
        ["changes scoring exactly the threshold"] = new(Changed(0.30), HalfAnHour, new ChapterSettings { Scene = 0.30 }),
        ["black over the quiet"] = new(
            new ChapterEvidence { Silences = [Quiet(At(300)), Quiet(At(360))], Blacks = [new ChapterSpan(At(299.8), At(300.2)), new ChapterSpan(At(359.8), At(360.2))] },
            HalfAnHour,
            new ChapterSettings()),
        ["changes too far from the quiet"] = new(
            new ChapterEvidence { Silences = [Quiet(At(300)), Quiet(At(360))], Scenes = [new ChapterScene(At(297), 0.9), new ChapterScene(At(357), 0.9)] },
            HalfAnHour,
            new ChapterSettings()),
        ["a pair further apart than a pod ever is"] = new(Observed(At(300), At(600)), TimeSpan.FromHours(1), new ChapterSettings()),
        ["what was observed past the end"] = new(Observed(At(1740), At(1800), At(1860)), HalfAnHour, new ChapterSettings()),
    };

    public static TheoryData<string> Scenarios => [.. Listed.Keys];

    [Theory(DisplayName = "the first version detects what the rule-based detection marks")]
    [MemberData(nameof(Scenarios))]
    public void TheFirstVersionDetectsWhatTheRuleBasedDetectionMarks(string scenario) => DetectsWhatTheRuleBasedDetectionMarks(scenario, SegmentModel.First);

    [Theory(DisplayName = "BR-CD-007: the version of four kinds made by hand, without the catalog's weights, detects what the rule-based detection marks")]
    [MemberData(nameof(Scenarios))]
    public void TheVersionOfFourKindsDetectsWhatTheRuleBasedDetectionMarks(string scenario) => DetectsWhatTheRuleBasedDetectionMarks(scenario, SegmentModel.FirstOfFourKinds);

    [Theory(DisplayName = "BR-CD-007: the version of four kinds made by hand that hears silence from -55 dBFS detects what the rule-based detection marks")]
    [MemberData(nameof(Scenarios))]
    public void TheVersionBelowPausesDetectsWhatTheRuleBasedDetectionMarks(string scenario) => DetectsWhatTheRuleBasedDetectionMarks(scenario, SegmentModel.FourKindsBelowPauses);

    [Theory(DisplayName = "BR-CD-007: the version of four kinds made by hand that tells the opening and the ending in any series detects on speech what the rule-based detection marks")]
    [MemberData(nameof(Scenarios))]
    public void TheVersionForAnySeriesDetectsWhatTheRuleBasedDetectionMarks(string scenario) => DetectsWhatTheRuleBasedDetectionMarks(scenario, SegmentModel.FourKindsInAnySeries, spoken: true);

    private static void DetectsWhatTheRuleBasedDetectionMarks(string scenario, SegmentModel made, bool spoken = false)
    {
        (ChapterEvidence evidence, TimeSpan length, ChapterSettings settings) = Listed[scenario];

        ChapterDetection marked = ChapterGrid.Mark(evidence, length, settings);
        SegmentDetection detected = SegmentDetector.Detect(Translated(evidence, length, spoken), Bare, Model(settings, made));

        Assert.Equal(StateOf(marked.Verdict), detected.State);
        Assert.Equal(marked.BreakShare, detected.CmShare, 9);
        Assert.Equal(
            marked.Segments.Select(segment => (segment.Starts, segment.Ends, segment.Kind is ChapterKind.Break ? SegmentKind.Break : SegmentKind.Programme)),
            detected.State is SegmentDetectionState.Marked ? detected.Segments.Select(segment => (segment.Starts, segment.Ends, segment.Kind)) : []);
    }

    [Fact(DisplayName = "the scenarios cover the rule-based detection's own expectations where they were written down")]
    public void TheScenariosHoldTheRuleBasedExpectations()
    {
        SegmentDetection pulled = Detected("a pod off the grid pulled onto it");
        SegmentDetection sliver = Detected("a sliver of programme between two pods");
        SegmentDetection early = Detected("a pod starting almost at the beginning");
        SegmentDetection late = Detected("what was observed past the end");
        SegmentDetection over = Detected("breaks taking more than half the length");

        Assert.Equal((At(300), At(360)), Break(pulled));
        Assert.Equal((At(300), At(421.5)), Break(sliver));
        Assert.Equal((TimeSpan.Zero, At(61.5)), Break(early));
        Assert.Equal(2, early.Segments.Count);
        Assert.Equal((At(1740), HalfAnHour), Break(late));
        Assert.Equal(SegmentDetectionState.Discarded, over.State);
        Assert.Equal(945d / 1800d, over.CmShare, 9);
        Assert.Equal(9, over.Segments.Count(segment => segment.Kind is SegmentKind.Break));
    }

    private static SegmentDetection Detected(string scenario)
    {
        (ChapterEvidence evidence, TimeSpan length, ChapterSettings settings) = Listed[scenario];

        return SegmentDetector.Detect(Translated(evidence, length), Bare, Model(settings, SegmentModel.First));
    }

    private static (TimeSpan Starts, TimeSpan Ends) Break(SegmentDetection detection)
    {
        SegmentSpan found = detection.Segments.Single(segment => segment.Kind is SegmentKind.Break);

        return (found.Starts, found.Ends);
    }

    private static SegmentEvidence Translated(ChapterEvidence evidence, TimeSpan length, bool spoken = false)
    {
        double reach = new[] { length }
            .Concat(evidence.Silences.Select(quiet => quiet.Ends))
            .Concat(evidence.Blacks.Select(black => black.Ends))
            .Concat(evidence.Scenes.Select(scene => scene.At))
            .Max()
            .TotalSeconds + 1;
        SyntheticReadings readings = new(length.TotalSeconds, reach);

        if (spoken)
        {
            readings.Spoken(0, reach);
        }

        foreach (ChapterSpan quiet in evidence.Silences)
        {
            readings.QuietAround((quiet.Starts + ((quiet.Ends - quiet.Starts) / 2)).TotalSeconds, (quiet.Ends - quiet.Starts).TotalSeconds, SyntheticReadings.Pause);
        }

        foreach (ChapterScene scene in evidence.Scenes)
        {
            readings.Cut(scene.At.TotalSeconds, scene.Score);
        }

        foreach (ChapterSpan black in evidence.Blacks)
        {
            readings.Black(black.Starts.TotalSeconds, black.Ends.TotalSeconds);
        }

        return readings.Evidence();
    }

    private static SegmentModel Model(ChapterSettings settings, SegmentModel made)
    {
        SegmentThresholds initial = made.Thresholds;

        return new SegmentModel(
            made.Weights,
            made.StationOffsets,
            made.SeriesOffsets,
            new SegmentThresholds(
                initial.SilenceDbfs,
                initial.BlackLevel,
                settings.Scene,
                settings.GridTolerance.TotalSeconds,
                initial.GridPenalty,
                initial.Lambda,
                initial.LambdaStation,
                initial.LambdaSeries),
            made.Standardisation);
    }

    private static SegmentDetectionState StateOf(ChapterVerdict verdict) => verdict switch
    {
        ChapterVerdict.Marked => SegmentDetectionState.Marked,
        ChapterVerdict.Discarded => SegmentDetectionState.Discarded,
        _ => SegmentDetectionState.NothingFound,
    };

    private static TimeSpan At(double second) => TimeSpan.FromSeconds(second);

    private static ChapterSpan Quiet(TimeSpan around) => new(around - TimeSpan.FromSeconds(0.25), around + TimeSpan.FromSeconds(0.25));

    private static ChapterEvidence Observed(params TimeSpan[] boundaries)
        => new()
        {
            Silences = [.. boundaries.Select(Quiet)],
            Scenes = [.. boundaries.Select(at => new ChapterScene(at, 0.9))],
        };

    private static ChapterEvidence Changed(double score)
        => new()
        {
            Silences = [Quiet(At(300)), Quiet(At(360))],
            Scenes = [new ChapterScene(At(300), score), new ChapterScene(At(360), score)],
        };

    private static ChapterEvidence Pods(int many, double every, double lasting)
        => Observed([.. Enumerable.Range(0, many).SelectMany(step => new[] { At(100 + (every * step)), At(100 + (every * step) + lasting) })]);

    private sealed record Scenario(ChapterEvidence Evidence, TimeSpan Length, ChapterSettings Settings);
}
