using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class RotationBackoffTests
{
    [Fact]
    public void TheDelayGrowsWithEveryConsecutiveFailure()
    {
        var backoff = new RotationBackoff(TimeSpan.FromMinutes(1), 2, TimeSpan.FromHours(1), 6);

        Assert.Equal(TimeSpan.FromMinutes(1), backoff.DelayAfter(1));
        Assert.Equal(TimeSpan.FromMinutes(2), backoff.DelayAfter(2));
        Assert.Equal(TimeSpan.FromMinutes(4), backoff.DelayAfter(3));
    }

    [Fact]
    public void TheDelayStopsGrowingAtTheMaximum()
    {
        var backoff = new RotationBackoff(TimeSpan.FromMinutes(1), 2, TimeSpan.FromMinutes(4), 20);

        Assert.Equal(TimeSpan.FromMinutes(4), backoff.DelayAfter(4));
        Assert.Equal(TimeSpan.FromMinutes(4), backoff.DelayAfter(19));
    }

    [Fact]
    public void ThereIsNoDelayToAskForBeforeTheFirstFailure()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RotationBackoff.Default.DelayAfter(0));
    }

    [Fact]
    public void AFactorThatDoesNotGrowIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RotationBackoff(TimeSpan.FromMinutes(1), 1, TimeSpan.FromHours(1), 6));
    }

    [Fact]
    public void ACeilingLowEnoughToSkipBackingOffIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RotationBackoff(TimeSpan.FromMinutes(1), 2, TimeSpan.FromHours(1), 1));
    }

    [Fact]
    public void TheDefaultBacksOffRatherThanRetryingAtAFixedInterval()
    {
        Assert.True(RotationBackoff.Default.DelayAfter(2) > RotationBackoff.Default.DelayAfter(1));
        Assert.True(RotationBackoff.Default.FailureCeiling > 1);
    }
}
