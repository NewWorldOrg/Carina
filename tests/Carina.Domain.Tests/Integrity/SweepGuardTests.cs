using Carina.Domain.Integrity;

namespace Carina.Domain.Tests.Integrity;

public sealed class SweepGuardTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 3, 0, 0, DateTimeKind.Utc);

    private static readonly IntegrityCheckId Walking = new(new Guid("6d1f0b20-0000-0000-0000-0000000000a1"));

    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    [Fact]
    public void ASweepIsAllowedWhenNoneHasEverRun()
    {
        SweepVerdict verdict = SweepGuard.Of(null, null, Now, Cooldown);

        Assert.True(verdict.IsAllowed);
        Assert.Equal(SweepRefusal.None, verdict.Refusal);
        Assert.Null(verdict.RunningId);
        Assert.Null(verdict.NotBefore);
    }

    [Fact]
    public void ASweepIsRefusedWhileOneIsWalkingAndTheRefusalNamesIt()
    {
        SweepVerdict verdict = SweepGuard.Of(Walking, null, Now, Cooldown);

        Assert.False(verdict.IsAllowed);
        Assert.Equal(SweepRefusal.OneIsAlreadyRunning, verdict.Refusal);
        Assert.Equal(Walking, verdict.RunningId);
        Assert.Null(verdict.NotBefore);
    }

    [Fact]
    public void TheOneWalkingIsNamedEvenWhenTheCooldownWouldAlsoRefuse()
    {
        SweepVerdict verdict = SweepGuard.Of(Walking, Now.AddSeconds(-1), Now, Cooldown);

        Assert.Equal(SweepRefusal.OneIsAlreadyRunning, verdict.Refusal);
        Assert.Equal(Walking, verdict.RunningId);
        Assert.Null(verdict.NotBefore);
    }

    [Fact]
    public void ASweepAskedForInsideTheCooldownIsRefusedAndSaysWhenItMayBeAskedForAgain()
    {
        DateTime finished = Now.AddMinutes(-1);

        SweepVerdict verdict = SweepGuard.Of(null, finished, Now, Cooldown);

        Assert.False(verdict.IsAllowed);
        Assert.Equal(SweepRefusal.TooSoonAfterTheLastOne, verdict.Refusal);
        Assert.Null(verdict.RunningId);
        Assert.Equal(finished + Cooldown, verdict.NotBefore);
    }

    [Fact]
    public void ASweepOneTickBeforeTheCooldownIsUpIsStillRefused()
    {
        DateTime finished = Now - Cooldown + TimeSpan.FromTicks(1);

        Assert.Equal(SweepRefusal.TooSoonAfterTheLastOne, SweepGuard.Of(null, finished, Now, Cooldown).Refusal);
    }

    [Fact]
    public void ASweepAtTheMomentTheCooldownIsUpIsAllowed()
    {
        DateTime finished = Now - Cooldown;

        Assert.True(SweepGuard.Of(null, finished, Now, Cooldown).IsAllowed);
    }

    [Fact]
    public void ASweepAfterTheCooldownIsAllowed()
    {
        Assert.True(SweepGuard.Of(null, Now.AddHours(-6), Now, Cooldown).IsAllowed);
    }

    [Fact]
    public void TheCooldownIsMeasuredFromTheLastFinishRatherThanFromAFixedInstant()
    {
        Assert.Equal(
            new DateTime(2026, 8, 26, 3, 1, 0, DateTimeKind.Utc),
            SweepGuard.Of(null, Now.AddMinutes(-4), Now, Cooldown).NotBefore);
        Assert.Equal(
            new DateTime(2026, 8, 26, 3, 3, 0, DateTimeKind.Utc),
            SweepGuard.Of(null, Now.AddMinutes(-2), Now, Cooldown).NotBefore);
    }

    [Fact]
    public void ALongerCooldownRefusesWhatAShorterOneWouldHaveAllowedAndSaysSoLaterOn()
    {
        DateTime finished = Now.AddMinutes(-10);

        Assert.True(SweepGuard.Of(null, finished, Now, TimeSpan.FromMinutes(5)).IsAllowed);
        Assert.False(SweepGuard.Of(null, finished, Now, TimeSpan.FromMinutes(30)).IsAllowed);
        Assert.Equal(
            new DateTime(2026, 8, 26, 3, 20, 0, DateTimeKind.Utc),
            SweepGuard.Of(null, finished, Now, TimeSpan.FromMinutes(30)).NotBefore);
    }
}
