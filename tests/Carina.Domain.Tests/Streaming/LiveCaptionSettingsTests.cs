using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveCaptionSettingsTests
{
    [Fact]
    public void ByDefaultACaptionIsNotMovedAgainstThePicture()
    {
        Assert.Equal(TimeSpan.Zero, new LiveCaptionSettings().EncoderDelay);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(-300)]
    [InlineData(10_000)]
    [InlineData(-10_000)]
    public void ACorrectionEitherWayWithinTenSecondsIsKept(int milliseconds)
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(milliseconds),
            new LiveCaptionSettings { EncoderDelay = TimeSpan.FromMilliseconds(milliseconds) }.EncoderDelay);
    }

    [Theory]
    [InlineData(10_001)]
    [InlineData(-10_001)]
    public void ACorrectionBeyondTenSecondsIsRefused(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LiveCaptionSettings { EncoderDelay = TimeSpan.FromMilliseconds(milliseconds) });
    }

    [Fact]
    public void WithNoCorrectionAStampIsHandedBackAsItWas()
    {
        Assert.Equal(LivePts.Of(6_908_746_875UL), new LiveCaptionSettings().Corrected(LivePts.Of(6_908_746_875UL)));
    }

    [Theory]
    [InlineData(300, 6_908_746_875UL, 6_908_773_875UL)]
    [InlineData(-300, 6_908_746_875UL, 6_908_719_875UL)]
    [InlineData(1000, 0UL, 90_000UL)]
    public void ACorrectionMovesTheStampByThatManyTicksOfTheNinetyKilohertzClock(int milliseconds, ulong stamped, ulong corrected)
    {
        LiveCaptionSettings settings = new() { EncoderDelay = TimeSpan.FromMilliseconds(milliseconds) };

        Assert.Equal(LivePts.Of(corrected), settings.Corrected(LivePts.Of(stamped)));
    }

    [Fact]
    public void ACorrectionThatWouldReachBeforeTheStartOfTheClockStopsAtTheStart()
    {
        LiveCaptionSettings settings = new() { EncoderDelay = TimeSpan.FromMilliseconds(-500) };

        Assert.Equal(LivePts.Start, settings.Corrected(LivePts.Of(44_999UL)));
        Assert.Equal(LivePts.Start, settings.Corrected(LivePts.Of(45_000UL)));
        Assert.Equal(LivePts.Of(1UL), settings.Corrected(LivePts.Of(45_001UL)));
    }

    [Fact]
    public void ACaptionerThatStartsCarriesNoFaultAndOneThatFailsNamesIts()
    {
        LiveCaptionerStart failed = LiveCaptionerStart.Failed(TranscoderFault.ProgrammeMissing, "'/usr/bin/ffmpeg' is not here");

        Assert.False(failed.Running);
        Assert.Null(failed.Captioner);
        Assert.Equal(TranscoderFault.ProgrammeMissing, failed.Fault);
        Assert.DoesNotContain('/', failed.Note);
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveCaptionerStart.Failed((TranscoderFault)99, "nothing"));
    }
}
