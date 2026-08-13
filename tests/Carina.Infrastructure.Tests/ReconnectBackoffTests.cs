using Carina.Infrastructure.Driver;

namespace Carina.Infrastructure.Tests;

public sealed class ReconnectBackoffTests
{
    [Fact]
    public void GrowsExponentiallyUntilTheCap()
    {
        var backoff = new ReconnectBackoff(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(1000),
            () => 1.0);

        Assert.Equal(
            [100, 200, 400, 800, 1000, 1000],
            [.. Enumerable.Range(0, 6).Select(_ => backoff.Next().TotalMilliseconds)]);
    }

    [Fact]
    public void JitterNeverGoesBelowHalfTheDelay()
    {
        var backoff = new ReconnectBackoff(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(1000),
            () => 0.0);

        Assert.Equal(50, backoff.Next().TotalMilliseconds);
        Assert.Equal(100, backoff.Next().TotalMilliseconds);
    }

    [Fact]
    public void AWildJitterSourceIsClampedIntoTheDelayWindow()
    {
        var backoff = new ReconnectBackoff(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(1000),
            () => 7.5);

        Assert.Equal(100, backoff.Next().TotalMilliseconds);
    }

    [Fact]
    public void ResetStartsTheClimbOver()
    {
        var backoff = new ReconnectBackoff(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(1000),
            () => 1.0);

        backoff.Next();
        backoff.Next();
        backoff.Reset();

        Assert.Equal(100, backoff.Next().TotalMilliseconds);
    }

    [Fact]
    public void ManyFailuresStayAtTheCapWithoutOverflowing()
    {
        var backoff = new ReconnectBackoff(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(30),
            () => 1.0);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            backoff.Next();
        }

        Assert.Equal(TimeSpan.FromSeconds(30), backoff.Next());
    }

    [Fact]
    public void RefusesASenselessWindow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ReconnectBackoff(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ReconnectBackoff(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
    }
}
