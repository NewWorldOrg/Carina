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

    [Fact(DisplayName = "the run of breaks is counted from its real length, so seven pieces of 14.6 seconds make 102.2 seconds, off the grid, and a doubtful eighth that brings it to 105 joins it")]
    public void TheRunIsCountedFromItsRealLength()
    {
        SegmentPiece[] pieces = Laid(300, 14.6, 14.6, 14.6, 14.6, 14.6, 14.6, 14.6, 2.8, 1394.4);
        double[] scores = [Programme, 2, 2, 2, 2, 2, 2, 2, -0.5, Programme];

        Assert.Equal(
            [SegmentKind.Programme, .. Enumerable.Repeat(SegmentKind.Break, 8), SegmentKind.Programme],
            SegmentSequence.Decide(pieces, scores, SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "a run of 15.0, 15.0 and 14.9 seconds is on the grid, so a doubtful piece that would take it off goes to the programme")]
    public void ARunJustShortOfTheGridIsOnIt()
    {
        SegmentPiece[] pieces = Laid(300, 15, 15, 14.9, 3, 1452.1);
        double[] scores = [Programme, 2, 2, 2, -0.5, Programme];

        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Break, SegmentKind.Break, SegmentKind.Break, SegmentKind.Programme, SegmentKind.Programme],
            SegmentSequence.Decide(pieces, scores, SegmentThresholds.Initial));
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

    [Fact(DisplayName = "BR-CD-007: a piece heard as the opening and one heard as the ending are decided as one each, the opening before the ending")]
    public void AnOpeningAndAnEndingAreDecided()
    {
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Programme, SegmentKind.Ending, SegmentKind.Programme],
            SegmentSequence.Decide(Laid(60, 90, 1500, 90, 60), [Plain, Opening(3), Plain, Ending(3), Plain], SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "BR-CD-007: there is one opening at most, so of two pieces apart that look like one the likelier is the opening and the other the programme")]
    public void ThereIsOneOpeningAtMost()
    {
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme],
            SegmentSequence.Decide(Laid(60, 90, 600, 90, 960), [Plain, Opening(3), Plain, Opening(2), Plain], SegmentThresholds.Initial));
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Ending, SegmentKind.Programme],
            SegmentSequence.Decide(Laid(60, 90, 600, 90, 960), [Plain, Ending(2), Plain, Ending(3), Plain], SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "BR-CD-007: the opening comes before the ending, so a piece like an opening after a likelier ending is the programme")]
    public void TheOpeningComesBeforeTheEnding()
    {
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Ending, SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme],
            SegmentSequence.Decide(Laid(60, 90, 1500, 90, 60), [Plain, Ending(3), Plain, Opening(2), Plain], SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "BR-CD-007: an opening or an ending lasts from 30 seconds to 3 minutes, so a piece like one that is shorter or longer is the programme, and neighbours join up to that length")]
    public void AnOpeningOrAnEndingLastsThirtySecondsToThreeMinutes()
    {
        Assert.Equal([SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme], SegmentSequence.Decide(Laid(60, 20, 1720), [Plain, Opening(3), Plain], SegmentThresholds.Initial));
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Opening, SegmentKind.Programme],
            SegmentSequence.Decide(Laid(60, 20, 20, 1700), [Plain, Opening(3), Opening(3), Plain], SegmentThresholds.Initial));
        Assert.Equal([SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme], SegmentSequence.Decide(Laid(1500, 200, 100), [Plain, Ending(3), Plain], SegmentThresholds.Initial));
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Ending, SegmentKind.Programme, SegmentKind.Programme],
            SegmentSequence.Decide(Laid(1500, 120, 90, 90), [Plain, Ending(4), Ending(3), Plain], SegmentThresholds.Initial));
        Assert.Equal([SegmentKind.Programme, SegmentKind.Programme], SegmentSequence.Decide(Laid(1780, 20), [Plain, Ending(3)], SegmentThresholds.Initial));
        Assert.Equal([SegmentKind.Programme, SegmentKind.Ending], SegmentSequence.Decide(Laid(1710, 90), [Plain, Ending(3)], SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "BR-CD-007: a break left for an opening off the grid costs the penalty as one left for the programme does")]
    public void ABreakLeftForAnOpeningOffTheGridCosts()
    {
        SegmentPiece[] pieces = Laid(300, 32, 90, 1378);
        SegmentChances[] chances = [Plain, Four(0.2, -8, -8), Opening(3), Plain];

        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Programme],
            SegmentSequence.Decide(pieces, chances, SegmentThresholds.Initial));
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Break, SegmentKind.Opening, SegmentKind.Programme],
            SegmentSequence.Decide(pieces, chances, Free));
    }

    [Fact(DisplayName = "with the opening and the ending all but impossible, four kinds decide what the break's scores alone decide")]
    public void FourKindsWithNoOpeningOrEndingDecideAsTwo()
    {
        SegmentPiece[] pieces = Laid(300, 14.6, 14.6, 14.6, 14.6, 14.6, 14.6, 14.6, 2.8, 1394.4);
        double[] scores = [Programme, 2, 2, 2, 2, 2, 2, 2, -0.5, Programme];
        SegmentChances[] four = [.. scores.Select(score => Four(score, -60, -60))];
        SegmentKind[] kinds = SegmentSequence.Decide(pieces, scores, SegmentThresholds.Initial);

        Assert.Equal(kinds, SegmentSequence.Decide(pieces, four, SegmentThresholds.Initial));
        Assert.Equal(
            SegmentSequence.Spans(pieces, kinds, scores, TimeSpan.FromSeconds(1800), SegmentThresholds.Initial).Select(span => (span.Starts, span.Ends, span.Kind, Math.Round(span.Probability!.Value, 9))),
            SegmentSequence.Spans(pieces, kinds, four, TimeSpan.FromSeconds(1800), SegmentThresholds.Initial).Select(span => (span.Starts, span.Ends, span.Kind, Math.Round(span.Probability!.Value, 9))));
    }

    [Fact(DisplayName = "an opening's probability is the mean of its pieces' chances of the opening weighted by length")]
    public void AnOpeningsProbabilityIsItsPiecesChance()
    {
        SegmentPiece[] pieces = Laid(60, 20, 40, 1680);
        SegmentChances[] chances = [Plain, Opening(3), Opening(1), Plain];

        IReadOnlyList<SegmentSpan> spans = SegmentSequence.Spans(
            pieces,
            [SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Opening, SegmentKind.Programme],
            chances,
            TimeSpan.FromSeconds(1800),
            SegmentThresholds.Initial);

        Assert.Equal((TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), SegmentKind.Opening), (spans[1].Starts, spans[1].Ends, spans[1].Kind));
        Assert.Equal(((20 * chances[1].Of(SegmentKind.Opening)) + (40 * chances[2].Of(SegmentKind.Opening))) / 60, spans[1].Probability!.Value, 12);
    }

    [Fact(DisplayName = "a score is needed for every piece")]
    public void AScoreIsNeededForEveryPiece()
        => Assert.Throws<ArgumentException>(() => SegmentSequence.Decide(Laid(300, 60), [Programme], SegmentThresholds.Initial));

    private static SegmentChances Plain => Four(-5, -8, -8);

    private static SegmentChances Opening(double score) => Four(-5, score, -8);

    private static SegmentChances Ending(double score) => Four(-5, -8, score);

    private static SegmentChances Four(double breaks, double opening, double ending)
        => SegmentChances.Of(new Dictionary<SegmentKind, double>
        {
            [SegmentKind.Break] = breaks,
            [SegmentKind.Opening] = opening,
            [SegmentKind.Ending] = ending,
        });

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
