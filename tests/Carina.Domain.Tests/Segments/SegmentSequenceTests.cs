using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentSequenceTests
{
    private const double Pod = 1.0;

    private const double Programme = -2.5;

    private static readonly SegmentThresholds Free = new(-50, 0.10, 0.30, 1.0, 0, 1, 10, 10);

    [Fact(DisplayName = "pieces that look like adverts and add up to the 15-second grid are breaks")]
    public void PiecesOnTheGridAreBreaks()
    {
        SegmentPiece[] pieces = Laid(300, 30, 30, 1440);

        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Break, SegmentKind.Break, SegmentKind.Programme],
            SegmentSequence.Decide(pieces, [Programme, Pod, Pod, Programme], SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "leaving a break off the grid costs the penalty, so a doubtful piece that would leave it off the grid goes to the programme")]
    public void LeavingABreakOffTheGridCosts()
    {
        SegmentPiece[] pieces = Laid(300, 30, 32, 1438);
        double[] scores = [Programme, Pod, 0.2, Programme];

        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Break, SegmentKind.Programme, SegmentKind.Programme],
            SegmentSequence.Decide(pieces, scores, SegmentThresholds.Initial));
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Break, SegmentKind.Break, SegmentKind.Programme],
            SegmentSequence.Decide(pieces, scores, Free));
    }

    [Fact(DisplayName = "a run of breaks within the tolerance of the grid, counted in whole seconds, leaves without cost")]
    public void ARunWithinTheToleranceLeavesFree()
    {
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Break, SegmentKind.Break, SegmentKind.Programme],
            SegmentSequence.Decide(Laid(300, 30, 30.9, 1439.1), [Programme, Pod, 0.2, Programme], SegmentThresholds.Initial));
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Break, SegmentKind.Programme, SegmentKind.Programme],
            SegmentSequence.Decide(Laid(300, 30, 31.6, 1438.4), [Programme, Pod, 0.2, Programme], SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "a break may start at the recording's zero and run on to its end without a cost")]
    public void ABreakMayStartAtZeroAndRunToTheEnd()
    {
        Assert.Equal(
            [SegmentKind.Break, SegmentKind.Programme, SegmentKind.Break],
            SegmentSequence.Decide(Laid(30, 1700, 70), [0.2, Programme, 0.2], SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "neighbouring pieces of one kind are one stretch, whose probability is the mean of theirs weighted by length")]
    public void NeighboursOfOneKindAreOneStretch()
    {
        SegmentPiece[] pieces = Laid(100, 50, 30, 30, 1590);
        double[] scores = [-2, -1, Pod, 0.5, Programme];

        IReadOnlyList<SegmentSpan> spans = SegmentSequence.Spans(
            pieces,
            [SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Break, SegmentKind.Break, SegmentKind.Programme],
            scores,
            TimeSpan.FromSeconds(1800),
            SegmentThresholds.Initial);

        Assert.Equal(3, spans.Count);
        Assert.Equal((TimeSpan.Zero, TimeSpan.FromSeconds(150), SegmentKind.Programme), (spans[0].Starts, spans[0].Ends, spans[0].Kind));
        Assert.Equal(((100 * (1 - SegmentModel.Chance(-2))) + (50 * (1 - SegmentModel.Chance(-1)))) / 150, spans[0].Probability!.Value, 12);
        Assert.Equal((SegmentModel.Chance(Pod) + SegmentModel.Chance(0.5)) / 2, spans[1].Probability!.Value, 12);
        Assert.Equal((TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(210), SegmentKind.Break), (spans[1].Starts, spans[1].Ends, spans[1].Kind));
    }

    [Fact(DisplayName = "a break off the grid within the tolerance is pulled onto it by its end, and one running to the recording's end is not")]
    public void ABreakOffTheGridIsPulledOntoIt()
    {
        SegmentKind[] kinds = [SegmentKind.Programme, SegmentKind.Break, SegmentKind.Programme, SegmentKind.Break];

        IReadOnlyList<SegmentSpan> spans = SegmentSequence.Spans(Laid(300, 60.7, 1378.6, 60.7), kinds, [Programme, Pod, Programme, Pod], TimeSpan.FromSeconds(1800), SegmentThresholds.Initial);

        Assert.Equal(TimeSpan.FromSeconds(360), spans[1].Ends);
        Assert.Equal(TimeSpan.FromSeconds(360), spans[2].Starts);
        Assert.Equal(TimeSpan.FromSeconds(1739.3), spans[3].Starts);
        Assert.Equal(TimeSpan.FromSeconds(1800), spans[3].Ends);
    }

    [Fact(DisplayName = "a break further off the grid than the tolerance stays where it was found")]
    public void ABreakFurtherOffStays()
    {
        IReadOnlyList<SegmentSpan> spans = SegmentSequence.Spans(
            Laid(300, 121.5, 1378.5),
            [SegmentKind.Programme, SegmentKind.Break, SegmentKind.Programme],
            [Programme, Pod, Programme],
            TimeSpan.FromSeconds(1800),
            SegmentThresholds.Initial);

        Assert.Equal(TimeSpan.FromSeconds(421.5), spans[1].Ends);
    }

    [Fact(DisplayName = "the boundaries between stretches are kept to a hundredth of a second")]
    public void BoundariesAreKeptToAHundredthOfASecond()
    {
        IReadOnlyList<SegmentSpan> spans = SegmentSequence.Spans(
            Laid(300.004, 1499.996),
            [SegmentKind.Programme, SegmentKind.Break],
            [Programme, Pod],
            TimeSpan.FromSeconds(1800),
            SegmentThresholds.Initial);

        Assert.Equal(TimeSpan.FromSeconds(300), spans[0].Ends);
        Assert.Equal(TimeSpan.FromSeconds(300), spans[1].Starts);
    }

    [Fact(DisplayName = "when a break and the programme score the same the programme is kept, and the same pieces give the same kinds every time")]
    public void TiesGoToTheProgrammeAndTheAnswerIsTheSame()
    {
        SegmentPiece[] pieces = Laid(300, 60, 1440);

        Assert.Equal([SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme], SegmentSequence.Decide(pieces, [Programme, 0, Programme], SegmentThresholds.Initial));
        Assert.Equal(SegmentSequence.Decide(pieces, [Programme, Pod, Programme], SegmentThresholds.Initial), SegmentSequence.Decide(pieces, [Programme, Pod, Programme], SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "a score is needed for every piece")]
    public void AScoreIsNeededForEveryPiece()
        => Assert.Throws<ArgumentException>(() => SegmentSequence.Decide(Laid(300, 60), [Programme], SegmentThresholds.Initial));

    private static SegmentPiece[] Laid(params double[] lengths)
    {
        List<SegmentPiece> pieces = [];
        TimeSpan reached = TimeSpan.Zero;

        foreach (double length in lengths)
        {
            TimeSpan ends = reached + TimeSpan.FromSeconds(length);

            pieces.Add(new SegmentPiece(reached, ends, ends - reached, SegmentFeatureValues.Of(new Dictionary<string, double?>())));
            reached = ends;
        }

        return [.. pieces];
    }
}
