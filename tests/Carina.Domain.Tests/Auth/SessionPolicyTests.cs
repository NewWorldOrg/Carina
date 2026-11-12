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

    [Fact(DisplayName = "BR-AU-005: an idle window may reach the absolute lifetime but not pass it")]
    public void AnIdleWindowFitsWithinTheAbsoluteLifetimeUpToItsEnd()
    {
        Assert.True(SessionPolicy.IdleFitsWithin(TimeSpan.FromDays(7), TimeSpan.FromDays(7)));
        Assert.True(SessionPolicy.IdleFitsWithin(TimeSpan.FromDays(7) - TimeSpan.FromTicks(1), TimeSpan.FromDays(7)));
        Assert.False(SessionPolicy.IdleFitsWithin(TimeSpan.FromDays(7) + TimeSpan.FromTicks(1), TimeSpan.FromDays(7)));
    }

    [Fact(DisplayName = "BR-AU-005: an idle window has to be longer than the time between the writes that say a session was used")]
    public void AnIdleWindowOutlastsTheWritesOnlyWhenLongerThanThem()
    {
        Assert.False(SessionPolicy.IdleOutlastsTheWrites(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30)));
        Assert.True(SessionPolicy.IdleOutlastsTheWrites(TimeSpan.FromMinutes(30) + TimeSpan.FromTicks(1), TimeSpan.FromMinutes(30)));
        Assert.False(SessionPolicy.IdleOutlastsTheWrites(TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(30)));
    }

    [Fact(DisplayName = "BR-AU-005: a policy is made exactly when both rules on its windows hold")]
    public void APolicyIsMadeExactlyWhenBothRulesOnItsWindowsHold()
    {
        TimeSpan lifetime = TimeSpan.FromDays(7);
        TimeSpan between = TimeSpan.FromMinutes(5);

        foreach (TimeSpan idle in new[] { between, between + TimeSpan.FromTicks(1), lifetime, lifetime + TimeSpan.FromTicks(1) })
        {
            bool holds = SessionPolicy.IdleFitsWithin(idle, lifetime) && SessionPolicy.IdleOutlastsTheWrites(idle, between);

            if (holds)
            {
                Assert.Equal(idle, new SessionPolicy(lifetime, idle, between).IdleTimeout);
            }
            else
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new SessionPolicy(lifetime, idle, between));
            }
        }
    }
}
