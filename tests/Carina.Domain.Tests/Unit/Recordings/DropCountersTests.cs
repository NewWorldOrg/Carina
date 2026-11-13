using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Unit.Recordings;

[Trait("Category", "Unit")]
public sealed class DropCountersTests
{
    [Fact]
    public void CountersThatNobodyTookCarryNoNumberAtAll()
    {
        DropCounters unmeasured = DropCounters.Unmeasured;

        Assert.False(unmeasured.Measured);
        Assert.Null(unmeasured.Dropped);
        Assert.Null(unmeasured.Total);
    }

    [Fact]
    public void CountingToZeroIsNotTheSameAsNotCounting()
    {
        DropCounters counted = DropCounters.Counted(0, 1_000_000);

        Assert.True(counted.Measured);
        Assert.Equal(0, counted.Dropped);
        Assert.Equal(1_000_000, counted.Total);
        Assert.NotEqual(DropCounters.Unmeasured, counted);
    }

    [Fact]
    public void CountersCannotLoseMorePacketsThanPassed()
        => Assert.Throws<ArgumentOutOfRangeException>(() => DropCounters.Counted(11, 10));

    [Fact]
    public void CountersCannotLoseANegativeNumberOfPackets()
        => Assert.Throws<ArgumentOutOfRangeException>(() => DropCounters.Counted(-1, 10));

    [Fact]
    public void RehydratingUnmeasuredCountersWithANumberIsRefused()
    {
        Assert.Throws<ArgumentException>(() => DropCounters.Rehydrate(false, 0, 0));
        Assert.Throws<ArgumentException>(() => DropCounters.Rehydrate(true, null, 10));
        Assert.Equal(DropCounters.Unmeasured, DropCounters.Rehydrate(false, null, null));
        Assert.Equal(DropCounters.Counted(3, 10), DropCounters.Rehydrate(true, 3, 10));
    }
}
