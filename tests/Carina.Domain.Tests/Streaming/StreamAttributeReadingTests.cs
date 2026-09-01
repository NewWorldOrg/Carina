using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class StreamAttributeReadingTests
{
    private static readonly StreamAttributes Measured = new(
        new VideoSize(1440, 1080),
        ScanType.Interlaced,
        FrameRate.Of(30000, 1001),
        AudioMode.Stereo);

    [Fact]
    public void AReadingWithNothingGuessedAtWasMeasured()
    {
        StreamAttributeReading reading = StreamAttributeReading.Read(Measured, []);

        Assert.True(reading.Measured);
        Assert.Null(reading.Fault);
        Assert.Empty(reading.FellBackOn);
    }

    [Fact]
    public void OneGuessIsEnoughToSayTheReadingWasNotMeasured()
    {
        StreamAttributeReading reading = StreamAttributeReading.Read(Measured, [StreamAttribute.Scan]);

        Assert.False(reading.Measured);
        Assert.True(reading.FellBack(StreamAttribute.Scan));
        Assert.False(reading.FellBack(StreamAttribute.Resolution));
        Assert.Null(reading.Fault);
    }

    [Fact]
    public void TheSameAttributeGuessedTwiceIsOneGuess()
    {
        StreamAttributeReading reading = StreamAttributeReading.Read(
            Measured,
            [StreamAttribute.Scan, StreamAttribute.Scan]);

        Assert.Equal([StreamAttribute.Scan], reading.FellBackOn);
    }

    [Fact]
    public void AProbeThatDidNotAnswerGuessedAtEverything()
    {
        StreamAttributeReading reading = StreamAttributeReading.Unanswered(
            StreamProbeFault.TimedOut,
            "the programme was still reading the stream");

        Assert.Equal(StreamProbeFault.TimedOut, reading.Fault);
        Assert.Equal(4, reading.FellBackOn.Count);
        Assert.All(Enum.GetValues<StreamAttribute>(), attribute => Assert.True(reading.FellBack(attribute)));
        Assert.Equal(StreamAttributes.SafeSide, reading.Attributes);
        Assert.Null(reading.ExitCode);
    }

    [Fact]
    public void ASafeSideAnswerIsTellableFromAMeasuredOneThatAgreesWithIt()
    {
        StreamAttributeReading guessed = StreamAttributeReading.Unanswered(StreamProbeFault.SaidNothing, "nothing");
        StreamAttributeReading read = StreamAttributeReading.Read(StreamAttributes.SafeSide, []);

        Assert.Equal(read.Attributes, guessed.Attributes);
        Assert.NotEqual(read.Measured, guessed.Measured);
    }

    [Fact]
    public void AProgrammeThatExitedZeroWasNotRefusedByIt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StreamAttributeReading.Refused(0, "quiet"));
    }

    [Fact]
    public void ARefusalCarriesItsCodeRatherThanBeingFiledWithoutOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StreamAttributeReading.Unanswered(StreamProbeFault.Refused, "quiet"));
    }

    [Fact]
    public void AFaultOutsideTheOnesNamedIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StreamAttributeReading.Unanswered((StreamProbeFault)99, "x"));
    }

    [Fact]
    public void AnAttributeOutsideTheOnesNamedIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StreamAttributeReading.Read(Measured, [(StreamAttribute)99]));
    }

    [Fact]
    public void ALongComplaintIsKeptFromTheEndWhereTheReasonIs()
    {
        string complaint = new string('x', StreamAttributeReading.LongestNote + 40) + "the reason";

        StreamAttributeReading reading = StreamAttributeReading.Refused(1, complaint);

        Assert.Equal(StreamAttributeReading.LongestNote, reading.Note.Length);
        Assert.EndsWith("the reason", reading.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSafeSideIsDeinterlacedStereoBroadcastHd()
    {
        Assert.Equal(1440, StreamAttributes.SafeSide.Size.Width);
        Assert.Equal(1080, StreamAttributes.SafeSide.Size.Height);
        Assert.Equal(ScanType.Interlaced, StreamAttributes.SafeSide.Scan);
        Assert.Equal(AudioMode.Stereo, StreamAttributes.SafeSide.Audio);
        Assert.Equal("30000/1001", StreamAttributes.SafeSide.Rate.ToString());
    }
}
