using Carina.Domain.Channels;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

/// <summary>
/// Scenarios written as readings, each with the breaks a reading laid on the grid adverts are aired on marks in it,
/// run through the versions of the model made by hand, of two kinds and of four, the catalog weighing in neither. The
/// version that tells the opening and the ending in any series hears speech between the cues, since sound that keeps
/// its loudness everywhere is a song.
/// </summary>
public sealed class SegmentDetectorRuleScenarioTests
{
    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    private static readonly double Length = HalfAnHour.TotalSeconds;

    private static readonly SegmentContext Bare = new(new NetworkId(1), new ServiceId(1), null, null, null, [], [], CornerLogo.None, []);

    private static readonly Marks NothingFound = new(SegmentDetectionState.NothingFound, 0, []);

    private static readonly Marks APodAtFiveMinutes = Marked(60 / Length, Programme(0, 300), Break(300, 360), Programme(360, 1800));

    private static readonly Dictionary<string, Scenario> Listed = new(StringComparer.Ordinal)
    {
        ["nothing observed"] = new(new Observation(), HalfAnHour, Tuning.Default, NothingFound),
        ["one boundary on its own"] = new(Observed(At(600)), HalfAnHour, Tuning.Default, NothingFound),
        ["quiet on its own every minute"] = new(new Observation { Silences = [.. Enumerable.Range(1, 20).Select(step => Quiet(At(60 * step)))] }, HalfAnHour, Tuning.Default, NothingFound),
        ["two quiet stretches a grid apart, one corroborated"] = new(
            new Observation { Silences = [Quiet(At(300)), Quiet(At(360))], Scenes = [new Scene(At(300), 0.9)] },
            HalfAnHour,
            Tuning.Default,
            NothingFound),
        ["a pod of adverts"] = new(Observed(At(300), At(360)), HalfAnHour, Tuning.Default, APodAtFiveMinutes),
        ["two pods"] = new(
            Observed(At(300), At(360), At(900), At(1020)),
            HalfAnHour,
            Tuning.Default,
            Marked(180 / Length, Programme(0, 300), Break(300, 360), Programme(360, 900), Break(900, 1020), Programme(1020, 1800))),
        ["two pods, the longer cut at every advert"] = new(
            Observed([At(300), At(360), .. Enumerable.Range(0, 9).Select(advert => At(900 + (15 * advert)))]),
            HalfAnHour,
            Tuning.Default,
            Marked(180 / Length, Programme(0, 300), Break(300, 360), Programme(360, 900), Break(900, 1020), Programme(1020, 1800))),
        ["a pair off the grid beyond the tolerance"] = new(Observed(At(300), At(362.5)), HalfAnHour, Tuning.Default, NothingFound),
        ["a pair just inside the tolerance"] = new(Observed(At(300), At(360.9)), HalfAnHour, Tuning.Default, APodAtFiveMinutes),
        ["a pair just outside the tolerance"] = new(Observed(At(300), At(361.1)), HalfAnHour, Tuning.Default, NothingFound),
        ["a pod off the grid pulled onto it"] = new(Observed(At(300), At(360.7)), HalfAnHour, Tuning.Default, APodAtFiveMinutes),
        ["two pods that overlap"] = new(
            Observed(At(300), At(330), At(360), At(420)),
            HalfAnHour,
            Tuning.Default,
            Marked(120 / Length, Programme(0, 300), Break(300, 420), Programme(420, 1800))),
        ["a sliver of programme between two pods"] = new(
            Observed(At(300), At(360), At(361.5), At(421.5)),
            HalfAnHour,
            Tuning.Default,
            Marked(121.5 / Length, Programme(0, 300), Break(300, 421.5), Programme(421.5, 1800))),
        ["a pod starting almost at the beginning"] = new(
            Observed(At(1.5), At(61.5)),
            HalfAnHour,
            Tuning.Default,
            Marked(61.5 / Length, Break(0, 61.5), Programme(61.5, 1800))),
        ["breaks taking more than half the length"] = new(
            Pods(9, every: 190, lasting: 105),
            HalfAnHour,
            Tuning.Default,
            new Marks(SegmentDetectionState.Discarded, 945 / Length, [])),
        ["a pair 20 seconds apart"] = new(Observed(At(300), At(320)), HalfAnHour, Tuning.Default, NothingFound),
        ["a pair two seconds off the grid"] = new(Observed(At(300), At(362)), HalfAnHour, Tuning.Default, NothingFound),
        ["a pair two seconds off the grid with a tolerance of three"] = new(
            Observed(At(300), At(362)),
            HalfAnHour,
            Tuning.Default with { GridTolerance = TimeSpan.FromSeconds(3) },
            APodAtFiveMinutes),
        ["changes of 0.20 at the quiet"] = new(Changed(0.20), HalfAnHour, Tuning.Default, NothingFound),
        ["changes of 0.20 at the quiet with a threshold of 0.10"] = new(Changed(0.20), HalfAnHour, Tuning.Default with { Scene = 0.10 }, APodAtFiveMinutes),
        ["changes scoring exactly the threshold"] = new(Changed(0.30), HalfAnHour, Tuning.Default with { Scene = 0.30 }, APodAtFiveMinutes),
        ["black over the quiet"] = new(
            new Observation { Silences = [Quiet(At(300)), Quiet(At(360))], Blacks = [new Stretch(At(299.8), At(300.2)), new Stretch(At(359.8), At(360.2))] },
            HalfAnHour,
            Tuning.Default,
            APodAtFiveMinutes),
        ["changes too far from the quiet"] = new(
            new Observation { Silences = [Quiet(At(300)), Quiet(At(360))], Scenes = [new Scene(At(297), 0.9), new Scene(At(357), 0.9)] },
            HalfAnHour,
            Tuning.Default,
            NothingFound),
        ["a pair further apart than a pod ever is"] = new(Observed(At(300), At(600)), TimeSpan.FromHours(1), Tuning.Default, NothingFound),
        ["what was observed past the end"] = new(
            Observed(At(1740), At(1800), At(1860)),
            HalfAnHour,
            Tuning.Default,
            Marked(60 / Length, Programme(0, 1740), Break(1740, 1800))),
    };

    /// <summary>
    /// The scenarios with a pod longer than a minute with no cut inside it, which no broadcast airs, since a pod is cut at
    /// every advert; the version that tells the opening and the ending in any series takes such a stretch for an opening
    /// or the programme rather than a break.
    /// </summary>
    private static readonly string[] UncutPods = ["two pods", "breaks taking more than half the length"];

    public static TheoryData<string> Scenarios => [.. Listed.Keys];

    public static TheoryData<string> ScenariosCutAtEveryAdvert => [.. Listed.Keys.Except(UncutPods)];

    [Theory(DisplayName = "the first version detects the breaks each scenario marks")]
    [MemberData(nameof(Scenarios))]
    public void TheFirstVersionDetectsWhatEachScenarioMarks(string scenario) => DetectsWhatTheScenarioMarks(scenario, SegmentModel.First);

    [Theory(DisplayName = "BR-CD-007: the version of four kinds made by hand, without the catalog's weights, detects the breaks each scenario marks")]
    [MemberData(nameof(Scenarios))]
    public void TheVersionOfFourKindsDetectsWhatEachScenarioMarks(string scenario) => DetectsWhatTheScenarioMarks(scenario, SegmentModel.FirstOfFourKinds);

    [Theory(DisplayName = "BR-CD-007: the version of four kinds made by hand that hears silence from -55 dBFS detects the breaks each scenario marks")]
    [MemberData(nameof(Scenarios))]
    public void TheVersionBelowPausesDetectsWhatEachScenarioMarks(string scenario) => DetectsWhatTheScenarioMarks(scenario, SegmentModel.FourKindsBelowPauses);

    [Theory(DisplayName = "BR-CD-007: the version of four kinds made by hand that tells the opening and the ending in any series detects on speech the breaks each scenario marks where every pod is cut at every advert")]
    [MemberData(nameof(ScenariosCutAtEveryAdvert))]
    public void TheVersionForAnySeriesDetectsWhatEachScenarioMarks(string scenario) => DetectsWhatTheScenarioMarks(scenario, SegmentModel.FourKindsInAnySeries, spoken: true);

    [Theory(DisplayName = "BR-CD-007: the version of four kinds made by hand with songs whose loudness moves detects on speech the breaks each scenario marks where every pod is cut at every advert")]
    [MemberData(nameof(ScenariosCutAtEveryAdvert))]
    public void TheVersionWithDynamicSongsDetectsWhatEachScenarioMarks(string scenario) => DetectsWhatTheScenarioMarks(scenario, SegmentModel.FourKindsWithDynamicSongs, spoken: true);

    [Theory(DisplayName = "BR-CD-007: the version of four kinds made by hand for pods of adverts detects on speech the breaks each scenario marks where every pod is cut at every advert")]
    [MemberData(nameof(ScenariosCutAtEveryAdvert))]
    public void TheVersionForPodsDetectsWhatEachScenarioMarks(string scenario) => DetectsWhatTheScenarioMarks(scenario, SegmentModel.FourKindsInPods, spoken: true);

    private static void DetectsWhatTheScenarioMarks(string scenario, SegmentModel made, bool spoken = false)
    {
        (Observation observed, TimeSpan length, Tuning tuning, Marks marked) = Listed[scenario];

        SegmentDetection detected = SegmentDetector.Detect(Translated(observed, length, spoken), Bare, Model(tuning, made));

        Assert.Equal(marked.State, detected.State);
        Assert.Equal(marked.Share, detected.CmShare, 9);
        Assert.Equal(
            marked.Segments,
            detected.State is SegmentDetectionState.Marked ? detected.Segments.Select(segment => (segment.Starts, segment.Ends, segment.Kind)) : []);
    }

    [Fact(DisplayName = "the first version places the breaks of the scenarios where a pod lies off the grid, near the ends or over half the length")]
    public void TheFirstVersionPlacesTheBreaksAtTheEdgesOfTheScenarios()
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
        (Observation observed, TimeSpan length, Tuning tuning, Marks _) = Listed[scenario];

        return SegmentDetector.Detect(Translated(observed, length), Bare, Model(tuning, SegmentModel.First));
    }

    private static (TimeSpan Starts, TimeSpan Ends) Break(SegmentDetection detection)
    {
        SegmentSpan found = detection.Segments.Single(segment => segment.Kind is SegmentKind.Break);

        return (found.Starts, found.Ends);
    }

    private static SegmentEvidence Translated(Observation evidence, TimeSpan length, bool spoken = false)
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

        foreach (Stretch quiet in evidence.Silences)
        {
            readings.QuietAround((quiet.Starts + ((quiet.Ends - quiet.Starts) / 2)).TotalSeconds, (quiet.Ends - quiet.Starts).TotalSeconds, SyntheticReadings.Pause);
        }

        foreach (Scene scene in evidence.Scenes)
        {
            readings.Cut(scene.At.TotalSeconds, scene.Score);
        }

        foreach (Stretch black in evidence.Blacks)
        {
            readings.Black(black.Starts.TotalSeconds, black.Ends.TotalSeconds);
        }

        return readings.Evidence();
    }

    private static SegmentModel Model(Tuning tuning, SegmentModel made)
    {
        SegmentThresholds initial = made.Thresholds;

        return new SegmentModel(
            made.Weights,
            made.StationOffsets,
            made.SeriesOffsets,
            new SegmentThresholds(
                initial.SilenceDbfs,
                initial.BlackLevel,
                tuning.Scene,
                tuning.GridTolerance.TotalSeconds,
                initial.GridPenalty,
                initial.Lambda,
                initial.LambdaStation,
                initial.LambdaSeries),
            made.Standardisation);
    }

    private static TimeSpan At(double second) => TimeSpan.FromSeconds(second);

    private static Stretch Quiet(TimeSpan around) => new(around - TimeSpan.FromSeconds(0.25), around + TimeSpan.FromSeconds(0.25));

    private static Observation Observed(params TimeSpan[] boundaries)
        => new()
        {
            Silences = [.. boundaries.Select(Quiet)],
            Scenes = [.. boundaries.Select(at => new Scene(at, 0.9))],
        };

    private static Observation Changed(double score)
        => new()
        {
            Silences = [Quiet(At(300)), Quiet(At(360))],
            Scenes = [new Scene(At(300), score), new Scene(At(360), score)],
        };

    private static Observation Pods(int many, double every, double lasting)
        => Observed([.. Enumerable.Range(0, many).SelectMany(step => new[] { At(100 + (every * step)), At(100 + (every * step) + lasting) })]);

    private static Marks Marked(double share, params (TimeSpan Starts, TimeSpan Ends, SegmentKind Kind)[] segments)
        => new(SegmentDetectionState.Marked, share, segments);

    private static (TimeSpan Starts, TimeSpan Ends, SegmentKind Kind) Programme(double starts, double ends) => (At(starts), At(ends), SegmentKind.Programme);

    private static (TimeSpan Starts, TimeSpan Ends, SegmentKind Kind) Break(double starts, double ends) => (At(starts), At(ends), SegmentKind.Break);

    private readonly record struct Stretch(TimeSpan Starts, TimeSpan Ends);

    private readonly record struct Scene(TimeSpan At, double Score);

    /// <summary>
    /// What a scenario observed: where it went quiet, where the picture went black and where it changed enough to be
    /// scored.
    /// </summary>
    private sealed record Observation
    {
        public IReadOnlyList<Stretch> Silences { get; init; } = [];

        public IReadOnlyList<Stretch> Blacks { get; init; } = [];

        public IReadOnlyList<Scene> Scenes { get; init; } = [];
    }

    /// <summary>
    /// How much the picture has to change at a boundary and how far off the grid a pair of boundaries may sit.
    /// </summary>
    private sealed record Tuning(double Scene, TimeSpan GridTolerance)
    {
        public static Tuning Default { get; } = new(0.30, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// What a scenario marks: the state, the share of the length taken for breaks, and the stretches when it marks any.
    /// </summary>
    private sealed record Marks(SegmentDetectionState State, double Share, IReadOnlyList<(TimeSpan Starts, TimeSpan Ends, SegmentKind Kind)> Segments);

    private sealed record Scenario(Observation Observed, TimeSpan Length, Tuning Tuning, Marks Marks);
}
