using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class BoostGuardTests
{
    private static readonly DateTime Now = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    [Fact]
    public void TheFirstBoostIsLetThrough()
        => Assert.True(BoostGuard.Of(null, null, Now, Cooldown).IsAllowed);

    [Fact]
    public void ASecondBoostIsRefusedAndNamesTheOneAlreadyWalking()
    {
        var running = Guid.NewGuid();
        BoostVerdict verdict = BoostGuard.Of(running, null, Now, Cooldown);

        Assert.Equal(BoostRefusal.OneIsAlreadyRunning, verdict.Refusal);
        Assert.Equal(running, verdict.RunningId);
    }

    [Fact]
    public void ABoostAskedForTooSoonSaysWhenItMayBeAskedForAgain()
    {
        BoostVerdict verdict = BoostGuard.Of(null, Now - TimeSpan.FromMinutes(4), Now, Cooldown);

        Assert.Equal(BoostRefusal.TooSoonAfterTheLastOne, verdict.Refusal);
        Assert.Equal(Now + TimeSpan.FromMinutes(6), verdict.NotBefore);
    }

    [Fact]
    public void TheMomentTheCooldownIsUpTheNextBoostIsLetThrough()
        => Assert.True(BoostGuard.Of(null, Now - Cooldown, Now, Cooldown).IsAllowed);

    [Fact]
    public void OneAlreadyRunningOutweighsTheCooldown()
    {
        var running = Guid.NewGuid();
        BoostVerdict verdict = BoostGuard.Of(running, Now - TimeSpan.FromMinutes(1), Now, Cooldown);

        Assert.Equal(BoostRefusal.OneIsAlreadyRunning, verdict.Refusal);
    }
}
