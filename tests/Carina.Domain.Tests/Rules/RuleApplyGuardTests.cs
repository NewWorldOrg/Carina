using Carina.Domain.Rules;

namespace Carina.Domain.Tests.Rules;

public sealed class RuleApplyGuardTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    [Fact]
    public void NothingRunningAndNothingFinishedYetLetsAnApplicationThrough()
    {
        RuleApplyVerdict verdict = RuleApplyGuard.Of(null, null, Now, Cooldown);

        Assert.True(verdict.IsAllowed);
        Assert.Equal(RuleApplyRefusal.None, verdict.Refusal);
        Assert.Null(verdict.RunningId);
        Assert.Null(verdict.NotBefore);
    }

    [Fact]
    public void OneAlreadyWalkingRefusesTheNextAndNamesTheOneThatIsWalking()
    {
        var walking = new Guid("0000000a-0000-0000-0000-000000000000");

        RuleApplyVerdict verdict = RuleApplyGuard.Of(walking, Now.AddHours(-1), Now, Cooldown);

        Assert.False(verdict.IsAllowed);
        Assert.Equal(RuleApplyRefusal.OneIsAlreadyRunning, verdict.Refusal);
        Assert.Equal(walking, verdict.RunningId);
    }

    [Fact]
    public void OneThatFinishedTooRecentlyRefusesTheNextAndSaysWhenItMayBeAskedForAgain()
    {
        DateTime finished = Now.AddSeconds(-30);

        RuleApplyVerdict verdict = RuleApplyGuard.Of(null, finished, Now, Cooldown);

        Assert.False(verdict.IsAllowed);
        Assert.Equal(RuleApplyRefusal.TooSoonAfterTheLastOne, verdict.Refusal);
        Assert.Equal(finished + Cooldown, verdict.NotBefore);
        Assert.Null(verdict.RunningId);
    }

    [Fact]
    public void OneThatFinishedLongEnoughAgoLetsTheNextThrough()
    {
        Assert.True(RuleApplyGuard.Of(null, Now - Cooldown, Now, Cooldown).IsAllowed);
    }

    [Fact]
    public void TheMomentTheCooldownIsUpIsTheFirstMomentItIsAllowed()
    {
        DateTime finished = Now - Cooldown;

        Assert.False(RuleApplyGuard.Of(null, finished.AddTicks(1), Now, Cooldown).IsAllowed);
        Assert.True(RuleApplyGuard.Of(null, finished, Now, Cooldown).IsAllowed);
    }

    [Fact]
    public void WhatIsRunningIsAnsweredBeforeTheCooldownIsWeighed()
    {
        var walking = new Guid("0000000b-0000-0000-0000-000000000000");

        RuleApplyVerdict verdict = RuleApplyGuard.Of(walking, Now.AddSeconds(-1), Now, Cooldown);

        Assert.Equal(RuleApplyRefusal.OneIsAlreadyRunning, verdict.Refusal);
    }
}
