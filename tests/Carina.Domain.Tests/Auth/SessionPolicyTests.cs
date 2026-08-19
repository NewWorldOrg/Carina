using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class SessionPolicyTests
{
    [Fact]
    public void TheDefaultPolicyLetsASessionIdleLongerThanAWeekendButNotForever()
    {
        SessionPolicy policy = SessionPolicy.Default;

        Assert.True(policy.IdleTimeout > TimeSpan.FromDays(2));
        Assert.True(policy.IdleTimeout <= policy.AbsoluteLifetime);
    }

    [Fact]
    public void TheDefaultPolicyThrottlesLastUsedWritesToMinutesRatherThanRequests()
    {
        Assert.True(SessionPolicy.Default.BetweenLastUsedWrites >= TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ALifetimeThatIsNotATimeIsNotAPolicy(int days)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SessionPolicy(TimeSpan.FromDays(days), TimeSpan.FromHours(1), TimeSpan.FromMinutes(5)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnIdleWindowThatIsNotATimeIsNotAPolicy(int hours)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SessionPolicy(TimeSpan.FromDays(30), TimeSpan.FromHours(hours), TimeSpan.FromMinutes(5)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AThrottleThatIsNotATimeIsNotAPolicy(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SessionPolicy(TimeSpan.FromDays(30), TimeSpan.FromDays(7), TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void AnIdleWindowOutlivingTheAbsoluteLifetimeCouldNeverBite()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SessionPolicy(TimeSpan.FromDays(7), TimeSpan.FromDays(8), TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void AThrottleAsLongAsTheIdleWindowWouldLogOutSomeoneWhoNeverLeft()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SessionPolicy(TimeSpan.FromDays(30), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void AnIdleWindowMayReachTheAbsoluteLifetimeExactly()
    {
        var policy = new SessionPolicy(TimeSpan.FromDays(7), TimeSpan.FromDays(7), TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromDays(7), policy.IdleTimeout);
    }

    [Fact]
    public void TwoPoliciesWithTheSameNumbersAreTheSamePolicy()
    {
        var policy = new SessionPolicy(TimeSpan.FromDays(30), TimeSpan.FromDays(7), TimeSpan.FromMinutes(5));
        var same = new SessionPolicy(TimeSpan.FromDays(30), TimeSpan.FromDays(7), TimeSpan.FromMinutes(5));

        Assert.Equal(policy, same);
    }
}
