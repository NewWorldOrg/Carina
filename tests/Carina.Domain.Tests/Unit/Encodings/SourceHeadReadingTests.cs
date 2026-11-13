using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Unit.Encodings;

[Trait("Category", "Unit")]
public sealed class SourceHeadReadingTests
{
    [Fact(DisplayName = "the head skip is the first decodable picture less the container's start, on the source's own clock")]
    public void TheHeadSkipIsTheFirstPictureLessTheStart()
    {
        SourceHeadReading reading = SourceHeadReading.Read(TimeSpan.FromSeconds(30499.474078), TimeSpan.FromSeconds(30499.981278));

        Assert.True(reading.Measured);
        Assert.Equal(TimeSpan.FromSeconds(0.5072), reading.HeadSkip);
        Assert.Null(reading.Fault);
    }

    [Fact(DisplayName = "a head that could not be read is a fault with a note, never a skip of nothing")]
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
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceHeadReading.Read(-EncodeTimeline.OneTurnOfTheClock, TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(0.5072), SourceHeadReading.Read(TimeSpan.FromSeconds(-3.6), TimeSpan.FromSeconds(-3.0928)).HeadSkip);
    }
}
