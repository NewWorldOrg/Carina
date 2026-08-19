namespace Carina.Domain.Auth;

public sealed record SessionPolicy
{
    public SessionPolicy(TimeSpan absoluteLifetime, TimeSpan idleTimeout, TimeSpan betweenLastUsedWrites)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(absoluteLifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(idleTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(betweenLastUsedWrites, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(idleTimeout, absoluteLifetime);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(betweenLastUsedWrites, idleTimeout);

        AbsoluteLifetime = absoluteLifetime;
        IdleTimeout = idleTimeout;
        BetweenLastUsedWrites = betweenLastUsedWrites;
    }

    public static SessionPolicy Default { get; } =
        new(TimeSpan.FromDays(30), TimeSpan.FromDays(7), TimeSpan.FromMinutes(5));

    public TimeSpan AbsoluteLifetime { get; }

    public TimeSpan IdleTimeout { get; }

    public TimeSpan BetweenLastUsedWrites { get; }
}
