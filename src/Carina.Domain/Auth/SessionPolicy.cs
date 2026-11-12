namespace Carina.Domain.Auth;

public sealed record SessionPolicy
{
    public SessionPolicy(TimeSpan absoluteLifetime, TimeSpan idleTimeout, TimeSpan betweenLastUsedWrites)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(absoluteLifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(idleTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(betweenLastUsedWrites, TimeSpan.Zero);

        if (!IdleFitsWithin(idleTimeout, absoluteLifetime))
        {
            throw new ArgumentOutOfRangeException(
                nameof(idleTimeout),
                idleTimeout,
                "A session cannot stay idle for longer than it may live at all.");
        }

        if (!IdleOutlastsTheWrites(idleTimeout, betweenLastUsedWrites))
        {
            throw new ArgumentOutOfRangeException(
                nameof(betweenLastUsedWrites),
                betweenLastUsedWrites,
                "A session is written as used more often than it may stay idle.");
        }

        AbsoluteLifetime = absoluteLifetime;
        IdleTimeout = idleTimeout;
        BetweenLastUsedWrites = betweenLastUsedWrites;
    }

    public static SessionPolicy Default { get; } =
        new(TimeSpan.FromDays(30), TimeSpan.FromDays(7), TimeSpan.FromMinutes(5));

    public TimeSpan AbsoluteLifetime { get; }

    public TimeSpan IdleTimeout { get; }

    public TimeSpan BetweenLastUsedWrites { get; }

    /// <summary>
    /// Whether a session may stay idle for <paramref name="idleTimeout"/> and still be cut off by the idle window
    /// before the absolute lifetime ends it.
    /// </summary>
    public static bool IdleFitsWithin(TimeSpan idleTimeout, TimeSpan absoluteLifetime) => idleTimeout <= absoluteLifetime;

    /// <summary>
    /// Whether a session in use is written as used before the idle window closes on it.
    /// </summary>
    public static bool IdleOutlastsTheWrites(TimeSpan idleTimeout, TimeSpan betweenLastUsedWrites)
        => idleTimeout > betweenLastUsedWrites;
}
