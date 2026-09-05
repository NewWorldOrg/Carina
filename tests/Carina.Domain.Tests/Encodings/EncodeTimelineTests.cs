using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeTimelineTests
{
    private static readonly TimeSpan SourceStart = TimeSpan.FromSeconds(30499.474078);

    private static readonly TimeSpan HeadSkip = TimeSpan.FromSeconds(0.5072);

    private static readonly TimeSpan SourceLength = TimeSpan.FromSeconds(2097.502489);

    [Fact(DisplayName = "BR-ED2-006: the artefact's zero on the source's clock is the source's start plus the head skipped — 30499.474078 + 0.5072 = 30499.981278, the first picture that could be decoded")]
    public void TheArtefactsZeroOnTheSourcesClockIsTheStartPlusTheHeadSkipped()
    {
        var timeline = new EncodeTimeline(SourceStart, HeadSkip, SourceLength, null);

        Assert.Equal(TimeSpan.FromSeconds(30499.981278), timeline.CaptionShift);
        Assert.Equal(TimeSpan.FromSeconds(2096.995289), timeline.Expected!.Value);
        Assert.Null(timeline.ArtefactLength);
        Assert.Null(timeline.Drift);
        Assert.Null(timeline.LengthsAgree);
    }

    [Fact(DisplayName = "BR-ED2-006: an artefact within a second of what the source had left agrees, one further off does not, and neither is anything but a reading")]
    public void AnArtefactWithinToleranceAgreesAndOneFurtherOffDoesNot()
    {
        var timeline = new EncodeTimeline(SourceStart, HeadSkip, SourceLength, null);

        EncodeTimeline agreeing = timeline.Measured(TimeSpan.FromSeconds(2096.7947));
        EncodeTimeline off = timeline.Measured(TimeSpan.FromSeconds(2094.5));
        EncodeTimeline longer = timeline.Measured(TimeSpan.FromSeconds(2098.1));

        Assert.Equal(TimeSpan.FromSeconds(-0.200589), agreeing.Drift!.Value);
        Assert.True(agreeing.LengthsAgree);
        Assert.False(off.LengthsAgree);
        Assert.False(longer.LengthsAgree);
        Assert.Equal(TimeSpan.FromSeconds(1), EncodeTimeline.Tolerance);
    }

    [Fact(DisplayName = "BR-ED2-006: a source whose length was not measured has nothing for the artefact to agree with, and says so rather than agreeing")]
    public void ASourceOfUnmeasuredLengthHasNothingToAgreeWith()
    {
        EncodeTimeline timeline = new EncodeTimeline(SourceStart, HeadSkip, null, null).Measured(TimeSpan.FromSeconds(2096.7947));

        Assert.Null(timeline.Expected);
        Assert.Null(timeline.Drift);
        Assert.Null(timeline.LengthsAgree);
        Assert.Equal(TimeSpan.FromSeconds(2096.7947), timeline.ArtefactLength);
    }

    [Fact(DisplayName = "BR-ED2-006: a head is skipped by between nothing and five seconds; the broadcast clock handed in as a skip is refused, because that is the seventeen hours")]
    public void AHeadIsSkippedByBetweenNothingAndFiveSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), EncodeTimeline.MostHeadSkip);
        Assert.True(EncodeTimeline.WithinReach(TimeSpan.Zero));
        Assert.True(EncodeTimeline.WithinReach(TimeSpan.FromSeconds(5)));
        Assert.False(EncodeTimeline.WithinReach(TimeSpan.FromSeconds(5.000001)));
        Assert.False(EncodeTimeline.WithinReach(TimeSpan.FromSeconds(62170)));
        Assert.False(EncodeTimeline.WithinReach(TimeSpan.FromSeconds(-0.001)));

        _ = new EncodeTimeline(TimeSpan.Zero, TimeSpan.Zero, null, null);
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodeTimeline(SourceStart, TimeSpan.FromSeconds(5.5), SourceLength, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodeTimeline(SourceStart, TimeSpan.FromSeconds(62170), SourceLength, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodeTimeline(SourceStart, TimeSpan.FromSeconds(-1), SourceLength, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodeTimeline(TimeSpan.FromSeconds(-1), HeadSkip, SourceLength, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodeTimeline(SourceStart, HeadSkip, TimeSpan.Zero, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EncodeTimeline(SourceStart, HeadSkip, SourceLength, TimeSpan.FromSeconds(-1)));
    }

    [Fact(DisplayName = "BR-ED2-006: a source shorter than the head skipped has nothing left to expect")]
    public void ASourceShorterThanTheHeadSkippedHasNothingLeftToExpect()
    {
        var timeline = new EncodeTimeline(SourceStart, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1));

        Assert.Null(timeline.Expected);
        Assert.Null(timeline.Drift);
        Assert.Null(timeline.LengthsAgree);
    }
}

public sealed class SourceHeadReadingTests
{
    [Fact(DisplayName = "BR-ED2-006: the head skip is the first decodable picture less the container's start, on the source's own clock")]
    public void TheHeadSkipIsTheFirstPictureLessTheStart()
    {
        SourceHeadReading reading = SourceHeadReading.Read(TimeSpan.FromSeconds(30499.474078), TimeSpan.FromSeconds(30499.981278));

        Assert.True(reading.Measured);
        Assert.Equal(TimeSpan.FromSeconds(0.5072), reading.HeadSkip);
        Assert.Null(reading.Fault);
    }

    [Fact(DisplayName = "BR-ED2-006: a head that could not be read is a fault with a note, never a skip of nothing")]
    public void AHeadThatCouldNotBeReadIsAFaultNeverASkipOfNothing()
    {
        SourceHeadReading refused = SourceHeadReading.Refused(1, "no such file");
        SourceHeadReading quiet = SourceHeadReading.Unanswered(SourceHeadFault.SaidNothing, "no picture");

        Assert.False(refused.Measured);
        Assert.Null(refused.HeadSkip);
        Assert.Equal(1, refused.ExitCode);
        Assert.Equal(SourceHeadFault.SaidNothing, quiet.Fault);
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceHeadReading.Refused(0, "fine"));
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceHeadReading.Unanswered(SourceHeadFault.Refused, "said"));
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceHeadReading.Unanswered((SourceHeadFault)9, "said"));
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceHeadReading.Read(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(9)));
    }
}
