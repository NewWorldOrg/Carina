using Carina.BroadcastTestSupport;
using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;

namespace Carina.Infrastructure.Tests.Auth;

public sealed class LoginThrottleTests
{
    private const string Caller = "10.0.0.9";

    private static readonly LoginRatePolicy Policy = new(5, TimeSpan.FromMinutes(5));

    private readonly HeldClock clock = new(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void ACallerThatHasNeverTriedIsNotHeldOff()
    {
        Assert.Null(Throttle().RefusesUntil(Caller));
    }

    [Fact]
    public void OneFewerWrongTryThanThePolicyAllowsStillGetsAnotherTry()
    {
        LoginThrottle throttle = Throttle();

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing - 1; attempt++)
        {
            throttle.Failed(Caller);
        }

        Assert.Null(throttle.RefusesUntil(Caller));
    }

    [Fact]
    public void TheWrongTryThatReachesThePolicyHoldsTheCallerOffUntilTheWindowHasPassed()
    {
        LoginThrottle throttle = Throttle();
        DateTime first = clock.GetUtcNow().UtcDateTime;

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing; attempt++)
        {
            throttle.Failed(Caller);
            clock.MoveOn(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(first + Policy.Window, throttle.RefusesUntil(Caller));
    }

    [Fact]
    public void TheHoldOffLiftsOnceTheOldestWrongTryHasFallenOutOfTheWindow()
    {
        LoginThrottle throttle = Throttle();

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing; attempt++)
        {
            throttle.Failed(Caller);
        }

        clock.MoveOn(Policy.Window);

        Assert.Null(throttle.RefusesUntil(Caller));
    }

    [Fact]
    public void ARightPasswordClearsWhatTheWrongOnesBuiltUp()
    {
        LoginThrottle throttle = Throttle();

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing - 1; attempt++)
        {
            throttle.Failed(Caller);
        }

        throttle.Passed(Caller);
        throttle.Failed(Caller);

        Assert.Null(throttle.RefusesUntil(Caller));
    }

    [Fact]
    public void OneCallerBeingHeldOffLeavesEveryOtherCallerAlone()
    {
        LoginThrottle throttle = Throttle();

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing; attempt++)
        {
            throttle.Failed(Caller);
        }

        Assert.NotNull(throttle.RefusesUntil(Caller));
        Assert.Null(throttle.RefusesUntil("10.0.0.10"));
    }

    private LoginThrottle Throttle() => new(Policy, clock);
}
