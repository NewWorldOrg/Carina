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

    [Fact(DisplayName = "BR-CD-007: a piece of the opening starts in the first half of the programme and one of the ending ends in its second half, so a piece like one on the wrong side is the programme unless the catalog fixes it to that kind, and an opening or an ending reaching over the middle stops there")]
    public void AnOpeningLiesInTheFirstHalfAndAnEndingInTheSecond()
    {
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme],
            SegmentSequence.Decide(Placed(60, 90, 1500, 90, 60), [Plain, Ending(3), Plain, Opening(2), Plain], SegmentThresholds.Initial));
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Programme, SegmentKind.Programme],
            SegmentSequence.Decide(Placed(850, 60, 60, 830), [Plain, Opening(3), Opening(3), Plain], SegmentThresholds.Initial));
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Ending, SegmentKind.Programme],
            SegmentSequence.Decide(Placed(830, 60, 60, 850), [Plain, Ending(3), Ending(3), Plain], SegmentThresholds.Initial));
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Programme],
            SegmentSequence.Decide(Fixed(Placed(60, 90, 1500, 90, 60), (3, SegmentKind.Opening, 0.8)), [Plain, Plain, Plain, Plain, Plain], SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "BR-CD-007: an opening long enough to leave is kept apart from a likelier one still too short, so the long one is chosen when the short one cannot last")]
    public void AnOpeningLongEnoughIsKeptApartFromAShorterOne()
    {
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Opening, SegmentKind.Programme],
            SegmentSequence.Decide(Laid(60, 20, 15, 1705), [Plain, Four(-5, -0.405, -8), Opening(4), Plain], SegmentThresholds.Initial));
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

    [Theory(DisplayName = "BR-CD-007: a piece the catalog fixes to a break is a break however the regression takes it, at its entry's confidence: 0.9 for an advert of two series, 0.5 for one of a single series, 0.6 for a fixed part of a station's output")]
    [InlineData(0.9)]
    [InlineData(0.5)]
    [InlineData(0.6)]
    public void APieceFixedToABreakIsABreak(double confidence)
    {
        SegmentPiece[] pieces = Fixed(Laid(300, 30, 1470), (1, SegmentKind.Break, confidence));
        double[] scores = [Programme, -8, Programme];
        SegmentKind[] kinds = SegmentSequence.Decide(pieces, scores, SegmentThresholds.Initial);

        Assert.Equal([SegmentKind.Programme, SegmentKind.Break, SegmentKind.Programme], kinds);
        Assert.Equal(confidence, SegmentSequence.Spans(pieces, kinds, scores, TimeSpan.FromSeconds(1800), SegmentThresholds.Initial)[1].Probability!.Value, 12);
        Assert.Equal([SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme], SegmentSequence.Decide(Laid(300, 30, 1470), scores, SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "BR-CD-007: a piece the catalog fixes to the programme, a fixed part of it, is the programme at 0.7 however likely a break the regression takes it for")]
    public void APieceFixedToTheProgrammeIsTheProgramme()
    {
        SegmentPiece[] pieces = Fixed(Laid(300, 30, 1470), (1, SegmentKind.Programme, 0.7));
        double[] scores = [Programme, 8, Programme];
        SegmentKind[] kinds = SegmentSequence.Decide(pieces, scores, SegmentThresholds.Initial);

        Assert.Equal([SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme], kinds);
        Assert.Equal(
            ((300 * (1 - SegmentModel.Chance(Programme))) + (30 * 0.7) + (1470 * (1 - SegmentModel.Chance(Programme)))) / 1800,
            SegmentSequence.Spans(pieces, kinds, scores, TimeSpan.FromSeconds(1800), SegmentThresholds.Initial)[0].Probability!.Value,
            12);
        Assert.Equal([SegmentKind.Programme, SegmentKind.Break, SegmentKind.Programme], SegmentSequence.Decide(Laid(300, 30, 1470), scores, SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "BR-CD-007: pieces the catalog fixes to the opening and the ending are those at their entries' confidence, and the pieces beside them are what the regression takes them for")]
    public void PiecesFixedToTheOpeningAndTheEndingAreThose()
    {
        SegmentPiece[] pieces = Fixed(Laid(60, 90, 30, 1500, 90, 30), (1, SegmentKind.Opening, 0.8), (4, SegmentKind.Ending, 0.8));
        SegmentChances[] chances = [Plain, Plain, Four(3, -8, -8), Plain, Plain, Plain];

        SegmentKind[] kinds = SegmentSequence.Decide(pieces, chances, SegmentThresholds.Initial);
        IReadOnlyList<SegmentSpan> spans = SegmentSequence.Spans(pieces, kinds, chances, TimeSpan.FromSeconds(1800), SegmentThresholds.Initial);

        Assert.Equal([SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Break, SegmentKind.Programme, SegmentKind.Ending, SegmentKind.Programme], kinds);
        Assert.Equal(
            [(SegmentKind.Opening, 0.8), (SegmentKind.Ending, 0.8)],
            spans.Where(span => span.Kind is SegmentKind.Opening or SegmentKind.Ending).Select(span => (span.Kind, span.Probability!.Value)));
        Assert.Equal([SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Break, SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme], SegmentSequence.Decide(Laid(60, 90, 30, 1500, 90, 30), chances, SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "BR-CD-007: a version that tells only breaks from the programme is never fixed to an opening or an ending, and decides as the break's scores alone do")]
    public void TwoKindsAreNeverFixedToAnOpening()
    {
        SegmentPiece[] pieces = Fixed(Laid(60, 90, 1500, 90, 60), (1, SegmentKind.Opening, 0.8), (3, SegmentKind.Ending, 0.8));
        double[] scores = [Programme, Programme, Programme, Pod, Programme];

        Assert.Equal(SegmentSequence.Decide(Laid(60, 90, 1500, 90, 60), scores, SegmentThresholds.Initial), SegmentSequence.Decide(pieces, scores, SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "BR-CD-007: a piece fixed to a break still sits in its run, so leaving the run off the grid costs the penalty and a doubtful neighbour that brings it onto the grid joins it")]
    public void AFixedBreakSitsInItsRun()
    {
        SegmentPiece[] pieces = Fixed(Laid(300, 13, 2, 1485), (1, SegmentKind.Break, 0.9));
        double[] scores = [Programme, Programme, -0.5, Programme];

        Assert.Equal([SegmentKind.Programme, SegmentKind.Break, SegmentKind.Break, SegmentKind.Programme], SegmentSequence.Decide(pieces, scores, SegmentThresholds.Initial));
        Assert.Equal([SegmentKind.Programme, SegmentKind.Break, SegmentKind.Programme, SegmentKind.Programme], SegmentSequence.Decide(pieces, scores, Free));
    }

    [Fact(DisplayName = "BR-CD-007: a piece fixed to the opening still lasts from 30 seconds to 3 minutes, so of its neighbours the one short enough joins it up to that length")]
    public void AFixedOpeningStillLastsThirtySeconds()
    {
        SegmentPiece[] pieces = Fixed(Laid(200, 20, 15, 1565), (1, SegmentKind.Opening, 0.8));

        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Opening, SegmentKind.Programme],
            SegmentSequence.Decide(pieces, [Plain, Plain, Plain, Plain], SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "BR-CD-007: the fixes still leave one opening before one ending, so of two pieces fixed to the opening the one the regression likes better keeps it, and of an ending fixed before an opening the likelier; the other is what the regression takes it for")]
    public void TheFixesLeaveOneOpeningBeforeOneEnding()
    {
        SegmentPiece[] two = Fixed(Laid(60, 90, 600, 90, 960), (1, SegmentKind.Opening, 0.8), (3, SegmentKind.Opening, 0.8));
        SegmentPiece[] reversed = Fixed(Laid(60, 90, 1500, 90, 60), (1, SegmentKind.Ending, 0.8), (3, SegmentKind.Opening, 0.8));
        SegmentChances[] laterLiked = [Plain, Plain, Plain, Opening(-3), Plain];

        SegmentKind[] kinds = SegmentSequence.Decide(two, laterLiked, SegmentThresholds.Initial);

        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme],
            SegmentSequence.Decide(two, [Plain, Opening(-3), Plain, Plain, Plain], SegmentThresholds.Initial));
        Assert.Equal([SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Programme], kinds);
        Assert.Equal(Plain.Of(SegmentKind.Programme), SegmentSequence.Spans(two, kinds, laterLiked, TimeSpan.FromSeconds(1800), SegmentThresholds.Initial)[0].Probability!.Value, 12);
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Ending, SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme],
            SegmentSequence.Decide(reversed, [Plain, Ending(-3), Plain, Plain, Plain], SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "BR-CD-007: a joined advert is taken in place of the segments it joins and counts as one segment, so an advert cut in three, each part likelier the programme alone, is a break in its pod, and without the joined advert the pod has a hole")]
    public void AJoinedAdvertFillsItsPod()
    {
        SegmentPiece[] pieces = Laid(300, 15, 4, 7, 4, 15, 1455);
        SegmentChances[] chances = [Plain, Four(2.5, -8, -8), Four(-2.5, -8, -8), Four(-2.5, -8, -8), Four(-1, -8, -8), Four(2.5, -8, -8), Plain];
        JoinedPiece advert = new(2, 4, new SegmentPiece(TimeSpan.FromSeconds(315), TimeSpan.FromSeconds(330), TimeSpan.FromSeconds(15), SegmentFeatureValues.Of(new Dictionary<string, double?>())), [SegmentKind.Break]);

        IReadOnlyList<SegmentSpan> joined = SegmentSequence.Decided(pieces, chances, [advert], [Four(2.5, -8, -8)], TimeSpan.FromSeconds(1800), SegmentThresholds.Initial);
        IReadOnlyList<SegmentSpan> apart = SegmentSequence.Decided(pieces, chances, [], [], TimeSpan.FromSeconds(1800), SegmentThresholds.Initial);

        Assert.Equal([(300d, 345d)], Breaks(joined));
        Assert.Equal([(300d, 315d), (330d, 345d)], Breaks(apart));
    }

    [Fact(DisplayName = "BR-CD-007: a joined opening counts as the segments it joins, each at the chance of the whole, so a song cut in three by the logo is one opening from its start where the whole is likelier than its parts, and the parts decide where it is not")]
    public void AJoinedOpeningIsTakenWhole()
    {
        SegmentPiece[] pieces = Laid(60, 15, 45, 30, 1650);
        SegmentChances[] chances = [Plain, Opening(-5), Opening(-1), Opening(1), Plain];
        SegmentPiece whole = new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(90), SegmentFeatureValues.Of(new Dictionary<string, double?>()));
        JoinedPiece song = new(1, 3, whole, [SegmentKind.Opening]);

        IReadOnlyList<SegmentSpan> likelier = SegmentSequence.Decided(pieces, chances, [song], [Opening(4)], TimeSpan.FromSeconds(1800), SegmentThresholds.Initial);
        IReadOnlyList<SegmentSpan> doubtful = SegmentSequence.Decided(pieces, chances, [song], [Opening(-3)], TimeSpan.FromSeconds(1800), SegmentThresholds.Initial);

        Assert.Equal([(60d, 150d)], Themes(likelier, SegmentKind.Opening));
        Assert.Equal([(120d, 150d)], Themes(doubtful, SegmentKind.Opening));
    }

    [Fact(DisplayName = "BR-CD-007: an opening or an ending does not run on across a seam, so a piece like one right after a seam starts nothing of the one before it, and a catalog fix across a seam is overruled for the fewer pieces")]
    public void AThemeDoesNotRunAcrossASeam()
    {
        SegmentPiece[] laid = Laid(60, 60, 30, 1650);
        SegmentPiece[] pieces = [laid[0], laid[1], laid[2] with { AfterASeam = true }, laid[3]];
        SegmentPiece[] fixedPieces = Fixed(pieces, (1, SegmentKind.Opening, 0.8), (2, SegmentKind.Opening, 0.8));

        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Programme, SegmentKind.Programme],
            SegmentSequence.Decide(pieces, [Plain, Opening(3), Opening(3), Plain], SegmentThresholds.Initial));
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Programme, SegmentKind.Programme],
            SegmentSequence.Decide(fixedPieces, [Plain, Plain, Plain, Plain], SegmentThresholds.Initial));
        Assert.Equal(
            [SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Opening, SegmentKind.Programme],
            SegmentSequence.Decide(laid, [Plain, Opening(3), Opening(3), Plain], SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "every joined piece is given its chances and joins segments there are")]
    public void AJoinedPieceIsGivenChancesOverSegmentsThereAre()
    {
        SegmentPiece[] pieces = Laid(300, 15, 15, 1470);
        JoinedPiece beyond = new(2, 4, pieces[2], [SegmentKind.Break]);

        Assert.Throws<ArgumentException>(() => SegmentSequence.Decided(pieces, [Plain, Plain, Plain, Plain], [beyond], [], TimeSpan.FromSeconds(1800), SegmentThresholds.Initial));
        Assert.Throws<ArgumentException>(() => SegmentSequence.Decided(pieces, [Plain, Plain, Plain, Plain], [beyond], [Plain], TimeSpan.FromSeconds(1800), SegmentThresholds.Initial));
    }

    [Fact(DisplayName = "a score is needed for every piece")]
    public void AScoreIsNeededForEveryPiece()
        => Assert.Throws<ArgumentException>(() => SegmentSequence.Decide(Laid(300, 60), [Programme], SegmentThresholds.Initial));

    private static SegmentChances Plain => Four(-5, -8, -8);

    private static (double Starts, double Ends)[] Breaks(IReadOnlyList<SegmentSpan> spans) => Themes(spans, SegmentKind.Break);

    private static (double Starts, double Ends)[] Themes(IReadOnlyList<SegmentSpan> spans, SegmentKind kind)
        => [.. spans.Where(span => span.Kind == kind).Select(span => (span.Starts.TotalSeconds, span.Ends.TotalSeconds))];

    private static SegmentChances Opening(double score) => Four(-5, score, -8);

    private static SegmentChances Ending(double score) => Four(-5, -8, score);

    private static SegmentChances Four(double breaks, double opening, double ending)
        => SegmentChances.Of(new Dictionary<SegmentKind, double>
        {
            [SegmentKind.Break] = breaks,
            [SegmentKind.Opening] = opening,
            [SegmentKind.Ending] = ending,
        });

    private static SegmentPiece[] Fixed(SegmentPiece[] pieces, params (int At, SegmentKind Kind, double Chance)[] fixes)
        => [.. pieces.Select((piece, at) => fixes.Where(fix => fix.At == at).Select(fix => piece with { Fixed = new CatalogFix(fix.Kind, fix.Chance) }).SingleOrDefault() ?? piece)];

    /// <summary>
    /// <see cref="Laid"/> pieces placed in a programme that spans them all.
    /// </summary>
    private static SegmentPiece[] Placed(params double[] lengths)
    {
        double programme = lengths.Sum();

        return
        [
            .. Laid(lengths).Select(piece => piece with
            {
                Features = SegmentFeatureValues.Of(new Dictionary<string, double?>
                {
                    [SegmentFeature.FromStart] = piece.Starts.TotalSeconds / programme,
                    [SegmentFeature.FromEnd] = (programme - piece.Ends.TotalSeconds) / programme,
                }),
            }),
        ];
    }

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
