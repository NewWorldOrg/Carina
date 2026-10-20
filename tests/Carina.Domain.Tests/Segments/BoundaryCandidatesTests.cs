using Carina.Domain.Channels;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class BoundaryCandidatesTests
{
    private const double Length = 600;

    private static readonly SegmentContext Bare = Context();

    [Fact(DisplayName = "sound quieter than -50 dBFS for 0.15 seconds or more is a silence at its middle; shorter, or at -50 dBFS, is not")]
    public void QuietLongEnoughIsASilence()
    {
        IReadOnlyList<BoundaryCandidate> found = Inner(new SyntheticReadings(Length)
            .Quiet(100, 100.16, 101)
            .Quiet(200, 200.14, 101)
            .Quiet(300, 300.5, 100));

        BoundaryCandidate silence = Assert.Single(found);
        Assert.Equal(BoundaryCue.Silence, silence.Cues);
        Assert.Equal(TimeSpan.FromSeconds(100.08), silence.At);
        Assert.Equal(TimeSpan.FromSeconds(0.16), silence.SilenceLength);
        Assert.Equal(0.5, silence.SilenceDepth, 9);
    }

    [Fact(DisplayName = "frames darker than a tenth of full scale for 0.15 seconds or more are black; brighter or briefer are not")]
    public void DarkLongEnoughIsBlack()
    {
        IReadOnlyList<BoundaryCandidate> found = Inner(new SyntheticReadings(Length)
            .Black(100, 100.2, 25)
            .Black(200, 200.4, 26)
            .Black(300, 300, 0));

        BoundaryCandidate black = Assert.Single(found);
        Assert.Equal(BoundaryCue.Black, black.Cues);
        Assert.Equal(TimeSpan.FromSeconds(100.15), black.At);
    }

    [Fact(DisplayName = "a change of scene is no candidate of its own, and corroborates a silence when the frame moved the threshold's share of full scale or more")]
    public void AChangeOfSceneOnlyCorroboratesASilence()
    {
        SyntheticReadings readings = new SyntheticReadings(Length)
            .Cut(50, 0.9)
            .QuietAround(100, level: SyntheticReadings.Pause).Cut(100, 0.3)
            .QuietAround(200, level: SyntheticReadings.Pause).Cut(200, 0.29)
            .QuietAround(300, level: SyntheticReadings.Pause).Cut(300, 0.2);

        Assert.Equal([true, false, false], [.. Inner(readings).Select(candidate => candidate.IsSilentCut)]);
        Assert.Equal([true, true, true], [.. Inner(readings, SegmentThresholds.Initial, sceneChange: 0.2).Select(candidate => candidate.IsSilentCut)]);
        Assert.Equal([TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(300)], [.. Inner(readings).Select(candidate => candidate.At)]);
    }

    [Fact(DisplayName = "a silence with a change of scene within half a second of its middle, or black over it, is a silent cut")]
    public void ACorroboratedSilenceIsASilentCut()
    {
        IReadOnlyList<BoundaryCandidate> found = Inner(new SyntheticReadings(Length)
            .QuietAround(100, level: SyntheticReadings.Pause).Cut(100.4, 0.9)
            .QuietAround(200, level: SyntheticReadings.Pause).Black(199.8, 200.2)
            .QuietAround(300, level: SyntheticReadings.Pause).Cut(300.8, 0.9));

        Assert.True(found[0].IsSilentCut);
        Assert.True(found[1].IsSilentCut);
        Assert.False(found[2].IsSilentCut);
        Assert.Equal(BoundaryCue.Silence | BoundaryCue.SilentCut, found[0].Cues);
        Assert.Equal(BoundaryCue.Silence | BoundaryCue.SilentCut | BoundaryCue.Black, found[1].Cues);
        Assert.Equal(BoundaryCue.Silence, found[2].Cues);
        Assert.Equal(TimeSpan.FromSeconds(300), found[2].At);
    }

    [Fact(DisplayName = "a silence as quiet on average as -90 dBFS, the silence an edit leaves, is a silent cut whatever the picture does, and a pause louder than that is not")]
    public void ASilenceAsQuietAsAnEditIsASilentCut()
    {
        IReadOnlyList<BoundaryCandidate> found = Inner(new SyntheticReadings(Length)
            .QuietAround(100)
            .QuietAround(200, level: 180)
            .QuietAround(300, level: 179)
            .QuietAround(400, level: SyntheticReadings.Pause));

        Assert.Equal([100d, 200, 300, 400], [.. found.Select(candidate => candidate.At.TotalSeconds)]);
        Assert.Equal([true, true, false, false], [.. found.Select(candidate => candidate.IsSilentCut)]);
    }

    [Fact(DisplayName = "the channels turning from alike to unlike, and captions coming and going, are no candidates")]
    public void ChannelsAndCaptionsAreNoCandidates()
        => Assert.Empty(Inner(new SyntheticReadings(Length).Channels(250, Length, 100).Captioned(second => second is (>= 50 and < 90) or (>= 300 and < 400))));

    [Fact(DisplayName = "the logo coming on or going off for more than a moment is a candidate, and a flicker is not")]
    public void TheLogoComingAndGoingIsACandidate()
    {
        bool[] seen = [.. Enumerable.Range(0, (int)Length).Select(second => second is (>= 100 and < 160) or 400)];

        IReadOnlyList<BoundaryCandidate> found = BoundaryCandidates.Find(new SyntheticReadings(Length).Evidence(), Bare, seen, SegmentThresholds.Initial);

        Assert.Equal([TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(160)], [.. found.Where(candidate => candidate.Cues is BoundaryCue.LogoChange).Select(candidate => candidate.At)]);
    }

    [Fact(DisplayName = "the edges of what the catalog heard as a kind it counts as and the programme's start and end by the guide are candidates, and the edges of a stretch of a kind it cannot tell are not")]
    public void CatalogAndGuideEdgesAreCandidates()
    {
        SegmentContext context = Context(
            starts: TimeSpan.FromSeconds(30),
            ends: TimeSpan.FromSeconds(570),
            catalog:
            [
                new CatalogHeard(TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(215), SegmentKind.Break, 3, 2, 1),
                new CatalogHeard(TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(327), null, 2, 2, 2),
            ]);

        IReadOnlyList<BoundaryCandidate> found = BoundaryCandidates.Find(new SyntheticReadings(Length).Evidence(), context, null, SegmentThresholds.Initial);

        Assert.Equal(
            [(30d, BoundaryCue.GuideEdge), (200d, BoundaryCue.CatalogEdge), (215d, BoundaryCue.CatalogEdge), (570d, BoundaryCue.GuideEdge)],
            [.. found.Skip(1).SkipLast(1).Select(candidate => (candidate.At.TotalSeconds, candidate.Cues))]);
    }

    [Fact(DisplayName = "candidates within two seconds are merged into the stronger, which keeps its second and gathers every cue")]
    public void CandidatesWithinTwoSecondsMergeIntoTheStronger()
    {
        IReadOnlyList<BoundaryCandidate> found = Inner(new SyntheticReadings(Length)
            .Black(98.4, 98.6).QuietAround(100).Cut(100, 0.9).QuietAround(101.9)
            .QuietAround(200).QuietAround(202.5));

        Assert.Equal([TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(202.5)], [.. found.Select(candidate => candidate.At)]);
        Assert.Equal(BoundaryCue.Black | BoundaryCue.Silence | BoundaryCue.SilentCut, found[0].Cues);
    }

    [Fact(DisplayName = "a merged candidate stretches over the silent cuts and catalog edges it took in, and is its own second otherwise")]
    public void AMergedCandidateStretchesOverItsCuts()
    {
        SegmentContext context = Context(catalog: [new CatalogHeard(TimeSpan.FromSeconds(301.2), TimeSpan.FromSeconds(316.2), SegmentKind.Break, 3, 2, 1)]);

        IReadOnlyList<BoundaryCandidate> found = BoundaryCandidates.Find(
            new SyntheticReadings(Length)
                .QuietAround(300).Cut(300, 0.9).QuietAround(301.9, level: SyntheticReadings.Pause)
                .QuietAround(400).Cut(400, 0.9).QuietAround(401.5, level: SyntheticReadings.Pause)
                .Evidence(),
            context,
            null,
            SegmentThresholds.Initial);
        BoundaryCandidate cut = found.Single(candidate => candidate.At == TimeSpan.FromSeconds(300));
        BoundaryCandidate alone = found.Single(candidate => candidate.At == TimeSpan.FromSeconds(400));

        Assert.Equal((TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(301.2)), (cut.Earliest, cut.Latest));
        Assert.Equal((TimeSpan.FromSeconds(400), TimeSpan.FromSeconds(400)), (alone.Earliest, alone.Latest));
    }

    [Fact(DisplayName = "the recording's own ends are candidates that take in what lies within two seconds of them, and nothing past the end is one")]
    public void TheEndsTakeInWhatIsNearThem()
    {
        IReadOnlyList<BoundaryCandidate> found = BoundaryCandidates.Find(
            new SyntheticReadings(Length, Length + 10).QuietAround(1.5).Cut(1.5, 0.9).QuietAround(598.5, level: SyntheticReadings.Pause).QuietAround(605).Evidence(),
            Bare,
            null,
            SegmentThresholds.Initial);

        Assert.Equal(2, found.Count);
        Assert.Equal((TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(1.5)), (found[0].At, found[0].Earliest, found[0].Latest));
        Assert.Equal(BoundaryCue.RecordingEdge | BoundaryCue.Silence | BoundaryCue.SilentCut, found[0].Cues);
        Assert.Equal(TimeSpan.FromSeconds(Length), found[1].At);
        Assert.Equal(BoundaryCue.RecordingEdge | BoundaryCue.Silence, found[1].Cues);
    }

    [Fact(DisplayName = "the snaps are the seconds of the merged candidates, without the recording's own ends")]
    public void TheSnapsAreTheMergedSeconds()
    {
        IReadOnlyList<BoundaryCandidate> found = BoundaryCandidates.Find(
            new SyntheticReadings(Length).QuietAround(1).QuietAround(100).QuietAround(101).QuietAround(250).Evidence(),
            Bare,
            null,
            SegmentThresholds.Initial);

        Assert.Equal([TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(250)], BoundaryCandidates.Snaps(found));
    }

    private static IReadOnlyList<BoundaryCandidate> Inner(SyntheticReadings readings)
        => Inner(readings, SegmentThresholds.Initial);

    private static IReadOnlyList<BoundaryCandidate> Inner(SyntheticReadings readings, SegmentThresholds thresholds, double? sceneChange = null)
    {
        SegmentThresholds used = sceneChange is double scene
            ? new SegmentThresholds(thresholds.SilenceDbfs, thresholds.BlackLevel, scene, thresholds.GridToleranceSec, thresholds.GridPenalty, thresholds.Lambda, thresholds.LambdaStation, thresholds.LambdaSeries)
            : thresholds;
        IReadOnlyList<BoundaryCandidate> found = BoundaryCandidates.Find(readings.Evidence(), Bare, null, used);

        Assert.True(found[0].Cues.HasFlag(BoundaryCue.RecordingEdge));
        Assert.True(found[^1].Cues.HasFlag(BoundaryCue.RecordingEdge));

        return [.. found.Skip(1).SkipLast(1)];
    }

    private static SegmentContext Context(TimeSpan? starts = null, TimeSpan? ends = null, IReadOnlyList<CatalogHeard>? catalog = null)
        => new(new NetworkId(1), new ServiceId(1), null, starts, ends, [], catalog ?? [], CornerLogo.None, []);
}
