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
        Assert.Null(Throttle().TakeTry(Caller));
    }

    [Fact]
    public void TheLastTryThePolicyAllowsStillGoesAhead()
    {
        LoginThrottle throttle = Throttle();

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing - 1; attempt++)
        {
            throttle.TakeTry(Caller);
        }

        Assert.Null(throttle.TakeTry(Caller));
    }

    [Fact]
    public void ATryPastThePolicyIsHeldOffUntilTheWindowHasPassed()
    {
        LoginThrottle throttle = Throttle();
        DateTime first = clock.GetUtcNow().UtcDateTime;

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing; attempt++)
        {
            throttle.TakeTry(Caller);
            clock.MoveOn(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(first + Policy.Window, throttle.TakeTry(Caller));
    }

    [Fact]
    public void TriesWhoseAnswerIsNotInYetAlreadyCountAgainstTheCaller()
    {
        LoginThrottle throttle = Throttle();

        DateTime?[] answered =
        [
            .. Enumerable.Range(0, Policy.FailuresBeforeRefusing + 3).Select(_ => throttle.TakeTry(Caller)),
        ];

        Assert.Equal(Policy.FailuresBeforeRefusing, answered.Count(until => until is null));
    }

    [Fact]
    public void ATryThatIsHeldOffDoesNotPushTheHoldOffFurtherOut()
    {
        LoginThrottle throttle = Throttle();
        DateTime first = clock.GetUtcNow().UtcDateTime;

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing; attempt++)
        {
            throttle.TakeTry(Caller);
        }

        clock.MoveOn(TimeSpan.FromMinutes(1));
        throttle.TakeTry(Caller);
        clock.MoveOn(TimeSpan.FromMinutes(1));

        Assert.Equal(first + Policy.Window, throttle.TakeTry(Caller));
    }

    [Fact]
    public void TheHoldOffLiftsOnceTheOldestTryHasFallenOutOfTheWindow()
    {
        LoginThrottle throttle = Throttle();

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing; attempt++)
        {
            throttle.TakeTry(Caller);
        }

        clock.MoveOn(Policy.Window);

        Assert.Null(throttle.TakeTry(Caller));
    }

    [Fact]
    public void ARightPasswordClearsWhatTheWrongOnesBuiltUp()
    {
        LoginThrottle throttle = Throttle();

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing; attempt++)
        {
            throttle.TakeTry(Caller);
        }

        throttle.Passed(Caller);

        Assert.Null(throttle.TakeTry(Caller));
    }

    [Fact]
    public void OneCallerBeingHeldOffLeavesEveryOtherCallerAlone()
    {
        LoginThrottle throttle = Throttle();

        for (int attempt = 0; attempt < Policy.FailuresBeforeRefusing; attempt++)
        {
            throttle.TakeTry(Caller);
        }

        Assert.NotNull(throttle.TakeTry(Caller));
        Assert.Null(throttle.TakeTry("10.0.0.10"));
    }

    private LoginThrottle Throttle() => new(Policy, clock);
}
