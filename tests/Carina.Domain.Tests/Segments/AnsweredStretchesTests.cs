using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class AnsweredStretchesTests
{
    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    [Fact(DisplayName = "BR-CV-001: stretches covering the recording from zero to its length are taken, in the recording's own time and with no probability")]
    public void StretchesCoveringTheRecordingAreTaken()
    {
        AnswerReading read = AnsweredStretches.Read(
            [Sent(0, 912.3, SegmentKind.Programme), Sent(912.3, 1002.3, SegmentKind.Break), Sent(1002.3, 1800, SegmentKind.Programme)],
            HalfAnHour);

        Assert.Null(read.Fault);
        Assert.Equal(HalfAnHour, read.Stretches!.Length);
        Assert.Equal(
            [
                new SegmentSpan(TimeSpan.Zero, TimeSpan.FromSeconds(912.3), SegmentKind.Programme, null),
                new SegmentSpan(TimeSpan.FromSeconds(912.3), TimeSpan.FromSeconds(1002.3), SegmentKind.Break, null),
                new SegmentSpan(TimeSpan.FromSeconds(1002.3), HalfAnHour, SegmentKind.Programme, null),
            ],
            read.Stretches.Stretches);
    }

    [Fact(DisplayName = "BR-CV-001: seconds are rounded to the hundredth, so boundaries that meet once rounded leave no gap, and the length is rounded the same way")]
    public void SecondsAreRoundedToTheHundredth()
    {
        TimeSpan written = TimeSpan.FromSeconds(1800.004);

        AnswerReading read = AnsweredStretches.Read(
            [Sent(0.001, 912.301, SegmentKind.Programme), Sent(912.299, 1002.305, SegmentKind.Break), Sent(1002.31, 1800, SegmentKind.Programme)],
            written);

        Assert.Null(read.Fault);
        Assert.Equal(HalfAnHour, read.Stretches!.Length);
        Assert.Equal(HalfAnHour, AnsweredStretches.LengthOf(written));
        Assert.Equal(
            [TimeSpan.Zero, TimeSpan.FromSeconds(912.3), TimeSpan.FromSeconds(1002.31)],
            read.Stretches.Stretches.Select(stretch => stretch.Starts));
    }

    [Fact(DisplayName = "BR-CV-001: two stretches of the same kind side by side are made one")]
    public void StretchesOfTheSameKindSideBySideAreMadeOne()
    {
        AnswerReading read = AnsweredStretches.Read(
            [
                Sent(0, 300, SegmentKind.Programme),
                Sent(300, 330, SegmentKind.Break),
                Sent(330, 360, SegmentKind.Break),
                Sent(360, 900, SegmentKind.Programme),
                Sent(900, 1800, SegmentKind.Programme),
            ],
            HalfAnHour);

        Assert.Equal(
            [(0d, 300d, SegmentKind.Programme), (300d, 360d, SegmentKind.Break), (360d, 1800d, SegmentKind.Programme)],
            read.Stretches!.Stretches.Select(stretch => (stretch.Starts.TotalSeconds, stretch.Ends.TotalSeconds, stretch.Kind)));
    }

    [Fact(DisplayName = "BR-CV-001: stretches are taken in the order they start, whatever order they were sent in")]
    public void StretchesAreTakenInTheOrderTheyStart()
    {
        AnswerReading read = AnsweredStretches.Read(
            [Sent(1002.3, 1800, SegmentKind.Programme), Sent(0, 912.3, SegmentKind.Programme), Sent(912.3, 1002.3, SegmentKind.Break)],
            HalfAnHour);

        Assert.Equal([0d, 912.3, 1002.3], read.Stretches!.Stretches.Select(stretch => stretch.Starts.TotalSeconds));
    }

    [Fact(DisplayName = "BR-CV-001: an answer is not held to the order a detection keeps: it may start and end on a break and give several in a row of programme between")]
    public void AnAnswerIsNotHeldToTheOrderADetectionKeeps()
    {
        AnswerReading read = AnsweredStretches.Read(
            [Sent(0, 60, SegmentKind.Break), Sent(60, 900, SegmentKind.Programme), Sent(900, 960, SegmentKind.Break), Sent(960, 1740, SegmentKind.Programme), Sent(1740, 1800, SegmentKind.Break)],
            HalfAnHour);

        Assert.Null(read.Fault);
        Assert.Equal(5, read.Stretches!.Stretches.Count);
    }

    [Theory(DisplayName = "BR-CV-001: stretches that leave part of the recording uncovered are refused as a gap")]
    [InlineData(0, 900, 901, 1800)]
    [InlineData(5, 900, 900, 1800)]
    [InlineData(0, 900, 900, 1799.9)]
    public void StretchesLeavingAGapAreRefused(double firstStarts, double firstEnds, double secondStarts, double secondEnds)
    {
        AnswerReading read = AnsweredStretches.Read(
            [Sent(firstStarts, firstEnds, SegmentKind.Programme), Sent(secondStarts, secondEnds, SegmentKind.Break)],
            HalfAnHour);

        Assert.Equal((AnswerFault.Gap, null), (read.Fault, read.Stretches));
    }

    [Fact(DisplayName = "BR-CV-001: no stretches at all leave the whole recording uncovered")]
    public void NoStretchesAtAllAreAGap()
        => Assert.Equal(AnswerFault.Gap, AnsweredStretches.Read([], HalfAnHour).Fault);

    [Theory(DisplayName = "BR-CV-001: stretches that cover part of the recording twice are refused as an overlap")]
    [InlineData(0, 901, 900, 1800)]
    [InlineData(0, 1800, 900, 1000)]
    [InlineData(0, 1800, 0, 1800)]
    public void StretchesOverlappingAreRefused(double firstStarts, double firstEnds, double secondStarts, double secondEnds)
    {
        AnswerReading read = AnsweredStretches.Read(
            [Sent(firstStarts, firstEnds, SegmentKind.Programme), Sent(secondStarts, secondEnds, SegmentKind.Break)],
            HalfAnHour);

        Assert.Equal(AnswerFault.Overlap, read.Fault);
    }

    [Theory(DisplayName = "BR-CV-001: a stretch before the recording's zero or past its length is refused as outside the recording")]
    [InlineData(-0.01, 1800)]
    [InlineData(0, 1800.01)]
    [InlineData(0, 1e300)]
    public void AStretchOutsideTheRecordingIsRefused(double starts, double ends)
        => Assert.Equal(AnswerFault.OutsideTheRecording, AnsweredStretches.Read([Sent(starts, ends, SegmentKind.Programme)], HalfAnHour).Fault);

    [Theory(DisplayName = "BR-CV-001: a stretch that ends where it starts, or before, is refused, even when it comes to that only once rounded")]
    [InlineData(900, 900)]
    [InlineData(900, 899)]
    [InlineData(900.001, 900.004)]
    public void AStretchThatEndsBeforeItStartsIsRefused(double starts, double ends)
    {
        AnswerReading read = AnsweredStretches.Read(
            [Sent(0, starts, SegmentKind.Programme), Sent(starts, ends, SegmentKind.Break), Sent(ends, 1800, SegmentKind.Programme)],
            HalfAnHour);

        Assert.Equal(AnswerFault.EndsBeforeItStarts, read.Fault);
    }

    [Theory(DisplayName = "BR-CV-001: a stretch with no kind, or a kind an answer does not give yet, is refused as an unknown kind")]
    [InlineData(null)]
    [InlineData(SegmentKind.Opening)]
    [InlineData(SegmentKind.Ending)]
    public void AStretchOfAnUnknownKindIsRefused(SegmentKind? kind)
    {
        AnswerReading read = AnsweredStretches.Read([Sent(0, 60, kind), Sent(60, 1800, SegmentKind.Programme)], HalfAnHour);

        Assert.Equal(AnswerFault.UnknownKind, read.Fault);
    }

    [Fact(DisplayName = "BR-CV-001: a stretch that is not there at all is refused as an unknown kind")]
    public void AStretchThatIsNotThereIsRefused()
        => Assert.Equal(AnswerFault.UnknownKind, AnsweredStretches.Read([Sent(0, 900, SegmentKind.Programme), null, Sent(900, 1800, SegmentKind.Programme)], HalfAnHour).Fault);

    [Fact(DisplayName = "BR-CV-001: the first fault found is answered, stretch by stretch in the order sent before the stretches are laid side by side")]
    public void TheFirstFaultFoundIsAnswered()
    {
        Assert.Equal(
            AnswerFault.EndsBeforeItStarts,
            AnsweredStretches.Read([Sent(0, 900, SegmentKind.Programme), Sent(1000, 950, SegmentKind.Break), Sent(950, 1800, SegmentKind.Opening)], HalfAnHour).Fault);
        Assert.Equal(
            AnswerFault.UnknownKind,
            AnsweredStretches.Read([Sent(0, 900, SegmentKind.Opening), Sent(1000, 950, SegmentKind.Break)], HalfAnHour).Fault);
        Assert.Equal(
            AnswerFault.OutsideTheRecording,
            AnsweredStretches.Read([Sent(0, 900, SegmentKind.Programme), Sent(1000, 1900, SegmentKind.Break)], HalfAnHour).Fault);
    }

    [Fact(DisplayName = "BR-CV-001: up to two hundred stretches an hour are taken, and one more is refused")]
    public void UpToTwoHundredStretchesAnHourAreTaken()
    {
        TimeSpan hour = TimeSpan.FromHours(1);

        Assert.Null(AnsweredStretches.Read(Alternating(200, hour), hour).Fault);
        Assert.Equal(AnswerFault.TooMany, AnsweredStretches.Read(Alternating(201, hour), hour).Fault);
        Assert.Null(AnsweredStretches.Read(Alternating(100, HalfAnHour), HalfAnHour).Fault);
        Assert.Equal(AnswerFault.TooMany, AnsweredStretches.Read(Alternating(101, HalfAnHour), HalfAnHour).Fault);
    }

    [Fact(DisplayName = "BR-CV-001: a recording too short for two hundred an hour to allow one stretch still takes an answer, and up to ten stretches")]
    public void AShortRecordingStillTakesAnAnswer()
    {
        TimeSpan tenSeconds = TimeSpan.FromSeconds(10);
        TimeSpan aMinute = TimeSpan.FromMinutes(1);

        AnswerReading single = AnsweredStretches.Read([Sent(0, 10, SegmentKind.Programme)], tenSeconds);

        Assert.Null(single.Fault);
        Assert.Single(single.Stretches!.Stretches);
        Assert.Null(AnsweredStretches.Read(Alternating(AnsweredStretches.FewestStretchesAllowed, tenSeconds), tenSeconds).Fault);
        Assert.Equal(AnswerFault.TooMany, AnsweredStretches.Read(Alternating(AnsweredStretches.FewestStretchesAllowed + 1, tenSeconds), tenSeconds).Fault);
        Assert.Null(AnsweredStretches.Read(Alternating(AnsweredStretches.FewestStretchesAllowed, aMinute), aMinute).Fault);
        Assert.Equal(AnswerFault.TooMany, AnsweredStretches.Read(Alternating(AnsweredStretches.FewestStretchesAllowed + 1, aMinute), aMinute).Fault);
    }

    [Fact(DisplayName = "BR-CV-001: stretches are counted once those of the same kind side by side are made one")]
    public void StretchesAreCountedOnceMerged()
    {
        SentStretch?[] pieces = [.. Enumerable.Range(0, 300).Select(at => Sent(at * 6, (at + 1) * 6, SegmentKind.Programme))];

        AnswerReading read = AnsweredStretches.Read(pieces, HalfAnHour);

        Assert.Null(read.Fault);
        Assert.Single(read.Stretches!.Stretches);
    }

    private static SentStretch?[] Alternating(int count, TimeSpan length)
    {
        double each = length.TotalSeconds / count;

        return
        [
            .. Enumerable.Range(0, count).Select(at => Sent(
                at * each,
                at == count - 1 ? length.TotalSeconds : (at + 1) * each,
                at % 2 == 0 ? SegmentKind.Programme : SegmentKind.Break)),
        ];
    }

    private static SentStretch Sent(double starts, double ends, SegmentKind? kind) => new(starts, ends, kind);
}
