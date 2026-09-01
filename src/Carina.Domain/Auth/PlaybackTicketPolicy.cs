namespace Carina.Domain.Auth;

public sealed record PlaybackTicketPolicy
{
    public static readonly TimeSpan LongestLifetime = TimeSpan.FromMinutes(2);

    public PlaybackTicketPolicy(TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lifetime, LongestLifetime);

        Lifetime = lifetime;
    }

    public static PlaybackTicketPolicy Default { get; } = new(TimeSpan.FromSeconds(30));

    public TimeSpan Lifetime { get; }
}
