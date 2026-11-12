using Carina.Domain.Notifications;

namespace Carina.Domain.Tests.Unit.Notifications;

[Trait("Category", "Unit")]
public sealed class NoticeRetryTests
{
    [Theory(DisplayName = "BR-NC-001: the next attempt follows the first failure by 30 seconds, the second by 2 minutes and the third by 10 minutes")]
    [InlineData(1, 30)]
    [InlineData(2, 120)]
    [InlineData(3, 600)]
    public void TheWaitGrowsWithEachFailure(int failedAttempts, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), NoticeRetry.After(failedAttempts));
    }

    [Fact(DisplayName = "BR-NS-001, BR-NC-001: no attempt follows the fourth failure, the one after the third wait")]
    public void NoAttemptFollowsTheFourthFailure()
    {
        Assert.Null(NoticeRetry.After(4));
        Assert.Equal(4, NoticeRetry.MostAttempts);
    }

    [Fact(DisplayName = "BR-NC-001: the wait the other end asks for is kept in place of the usual one")]
    public void TheWaitTheOtherEndAsksForIsKept()
    {
        Assert.Equal(TimeSpan.FromSeconds(7), NoticeRetry.After(1, TimeSpan.FromSeconds(7)));
    }

    [Fact(DisplayName = "BR-NC-001: the wait the other end asks for is never longer than 10 minutes")]
    public void TheWaitTheOtherEndAsksForIsCapped()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), NoticeRetry.After(1, TimeSpan.FromMinutes(10)));
        Assert.Equal(TimeSpan.FromMinutes(10), NoticeRetry.After(2, TimeSpan.FromHours(3)));
    }

    [Fact(DisplayName = "BR-NS-001, BR-NC-001: a wait asked for after the fourth failure does not bring another attempt")]
    public void AWaitAskedForAfterTheLastAttemptDoesNotBringAnother()
    {
        Assert.Null(NoticeRetry.After(4, TimeSpan.FromSeconds(1)));
    }

    [Fact(DisplayName = "BR-NC-001: a wait follows at least one failure, and a wait asked for is never negative")]
    public void AWaitFollowsAtLeastOneFailureAndIsNeverNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NoticeRetry.After(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NoticeRetry.After(1, TimeSpan.FromSeconds(-1)));
    }
}
