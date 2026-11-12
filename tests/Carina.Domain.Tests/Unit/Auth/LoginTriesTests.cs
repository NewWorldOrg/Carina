using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Unit.Auth;

[Trait("Category", "Unit")]
public sealed class LoginTriesTests
{
    private static readonly LoginRatePolicy Policy = new(3, TimeSpan.FromMinutes(5));

    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EveryTryThePolicyAllowsGoesAhead()
    {
        var tries = new LoginTries();

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing; attempt++)
        {
            Assert.Null(tries.Take(At.AddSeconds(attempt), Policy));
        }
    }

    [Fact]
    public void ATryPastThePolicyIsHeldOffUntilTheOldestTryLeavesTheWindow()
    {
        var tries = new LoginTries();

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing; attempt++)
        {
            tries.Take(At.AddSeconds(attempt), Policy);
        }

        Assert.Equal(At + Policy.Window, tries.Take(At.AddSeconds(10), Policy));
    }

    [Fact]
    public void ATryThatIsHeldOffIsNotCountedItself()
    {
        var tries = new LoginTries();

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing; attempt++)
        {
            tries.Take(At, Policy);
        }

        tries.Take(At.AddMinutes(1), Policy);
        tries.Take(At.AddMinutes(2), Policy);

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing; attempt++)
        {
            Assert.Null(tries.Take(At + Policy.Window, Policy));
        }
    }

    [Fact]
    public void ATryExactlyAWindowOldIsForgotten()
    {
        var tries = new LoginTries();

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing; attempt++)
        {
            tries.Take(At, Policy);
        }

        Assert.Equal(At + Policy.Window, tries.Take(At + Policy.Window - TimeSpan.FromTicks(1), Policy));
        Assert.Null(tries.Take(At + Policy.Window, Policy));
    }

    [Fact]
    public void ATimeThatIsNotUtcIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new LoginTries().Take(DateTime.SpecifyKind(At, DateTimeKind.Local), Policy));
    }
}
