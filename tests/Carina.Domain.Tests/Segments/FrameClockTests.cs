using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class FrameClockTests
{
    private static readonly FrameClock Broadcast = FrameClock.Of(30000, 1001, TimeSpan.FromMilliseconds(200));

    [Fact(DisplayName = "frame n falls n frame lengths after the first frame")]
    public void AFrameFallsWhereItsCountPutsIt()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(200), Broadcast.At(0));
        Assert.Equal(TimeSpan.FromMilliseconds(1201), Broadcast.At(30));
        Assert.Equal(TimeSpan.FromMilliseconds(1001200), Broadcast.At(30000));
    }

    [Fact(DisplayName = "the first frame at or after a time is counted exactly, a frame landing on the time included")]
    public void TheFirstFrameFromATimeIsCountedExactly()
    {
        Assert.Equal(0, Broadcast.FirstFrameFrom(TimeSpan.Zero));
        Assert.Equal(0, Broadcast.FirstFrameFrom(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(1, Broadcast.FirstFrameFrom(TimeSpan.FromMilliseconds(200) + TimeSpan.FromTicks(1)));
        Assert.Equal(30, Broadcast.FirstFrameFrom(TimeSpan.FromMilliseconds(1201)));
        Assert.Equal(31, Broadcast.FirstFrameFrom(TimeSpan.FromMilliseconds(1201) + TimeSpan.FromTicks(1)));
        Assert.Equal(17977, Broadcast.FirstFrameFrom(TimeSpan.FromSeconds(600)));
    }

    [Fact(DisplayName = "a clock taken from a time starts at the first frame at or after it, at the same rate")]
    public void AClockFromATimeStartsAtTheNextFrame()
    {
        FrameClock from = Broadcast.From(TimeSpan.FromSeconds(600));

        Assert.Equal(Broadcast.At(17977), from.FirstFrameAt);
        Assert.True(from.FirstFrameAt >= TimeSpan.FromSeconds(600));
        Assert.Equal((30000, 1001), (from.Numerator, from.Denominator));
        Assert.Equal(Broadcast, Broadcast.From(TimeSpan.Zero));
    }

    [Theory(DisplayName = "the most frames a stretch can hold is its length at the rate, rounded up")]
    [InlineData(30000, 1001, 600, 17983)]
    [InlineData(30, 1, 600, 18000)]
    [InlineData(24000, 1001, 1, 24)]
    public void TheMostFramesAStretchHoldsIsRoundedUp(int numerator, int denominator, int seconds, int frames)
        => Assert.Equal(frames, FrameClock.Of(numerator, denominator, TimeSpan.Zero).MostFramesIn(seconds));

    [Theory(DisplayName = "a rate of nothing, a rate faster than any picture stream and a first frame before time zero are refused")]
    [InlineData(0, 1, 0)]
    [InlineData(30, 0, 0)]
    [InlineData(-30, 1, 0)]
    [InlineData(241, 1, 0)]
    [InlineData(30, 1, -1)]
    public void ImpossibleClocksAreRefused(int numerator, int denominator, long firstFrameTicks)
        => Assert.Throws<ArgumentException>(() => FrameClock.Of(numerator, denominator, TimeSpan.FromTicks(firstFrameTicks)));
}
