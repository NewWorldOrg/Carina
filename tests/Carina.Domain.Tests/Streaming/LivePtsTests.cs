using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LivePtsTests
{
    [Fact]
    public void ThePresentationClockRunsAtTheRateTheBroadcastKeeps()
    {
        Assert.Equal(90_000, LivePts.Hertz);
    }

    [Fact]
    public void TheStartIsZero()
    {
        Assert.Equal(0UL, LivePts.Start.Value);
    }

    [Fact]
    public void TheFurthestIsEverythingEightBytesCanHold()
    {
        Assert.Equal(ulong.MaxValue, LivePts.Furthest.Value);
    }

    [Fact]
    public void TheBroadcastClockComesAroundAfterThirtyThreeBits()
    {
        Assert.Equal(8_589_934_592UL, LivePts.ComesAroundAt);
    }

    [Fact]
    public void TwoReadingsOfTheSameInstantAreTheSame()
    {
        Assert.Equal(LivePts.Of(90_000), LivePts.Of(90_000));
        Assert.NotEqual(LivePts.Of(90_000), LivePts.Of(90_001));
    }

    [Theory]
    [InlineData(0UL, 90_000U, 0UL)]
    [InlineData(1UL, 90_000U, 1UL)]
    [InlineData(1UL, 1_000U, 90UL)]
    [InlineData(15_360UL, 15_360U, 90_000UL)]
    [InlineData(48_000UL, 48_000U, 90_000UL)]
    [InlineData(1_024UL, 48_000U, 1_920UL)]
    public void AClockOfAnyRateIsReadAtNinetyKilohertz(ulong ticks, uint timescale, ulong expected)
    {
        Assert.Equal(expected, LivePts.Rescaled(ticks, timescale).Value);
    }

    [Fact]
    public void ARescalingThatWouldOverflowSixtyFourBitsOnTheWayIsStillAnswered()
    {
        Assert.Equal(ulong.MaxValue / 90_000UL * 90_000UL, LivePts.Rescaled(ulong.MaxValue / 90_000UL, 1U).Value);
    }

    [Fact]
    public void AClockWithNoRateIsNotAClock()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LivePts.Rescaled(1UL, 0U));
    }

    [Fact]
    public void AReadingPastWhereTheBroadcastClockComesAroundIsKeptWhole()
    {
        Assert.Equal(LivePts.ComesAroundAt, LivePts.Of(LivePts.ComesAroundAt).Value);
        Assert.Equal(LivePts.ComesAroundAt + 1UL, LivePts.Of(LivePts.ComesAroundAt + 1UL).Value);
    }
}
