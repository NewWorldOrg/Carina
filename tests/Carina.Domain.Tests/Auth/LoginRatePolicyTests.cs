using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class LoginRatePolicyTests
{
    [Fact]
    public void TheDefaultAllowsAHandfulOfWrongTriesInsideAShortWindow()
    {
        Assert.Equal(5, LoginRatePolicy.Default.FailuresBeforeRefusing);
        Assert.Equal(TimeSpan.FromMinutes(5), LoginRatePolicy.Default.Window);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APolicyThatRefusesBeforeTheFirstTryWouldLockEveryoneOut(int failures)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoginRatePolicy(failures, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void APolicyWithoutAWindowWouldNeverForgiveAWrongTry()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoginRatePolicy(5, TimeSpan.Zero));
    }
}
