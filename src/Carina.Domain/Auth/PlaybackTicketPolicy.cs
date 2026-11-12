namespace Carina.Domain.Auth;

public sealed record PlaybackTicketPolicy
{
    public const int MostHeldPerSubject = 8;

    public static readonly TimeSpan LongestLifetime = TimeSpan.FromMinutes(2);

    public PlaybackTicketPolicy(TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lifetime, LongestLifetime);

        Lifetime = lifetime;
    }

    public static PlaybackTicketPolicy Default { get; } = new(TimeSpan.FromSeconds(30));

    public TimeSpan Lifetime { get; }

    /// <summary>
    /// Whether another ticket may be issued to <paramref name="subject"/> beside those <paramref name="held"/>: a
    /// subject holds at most <see cref="MostHeldPerSubject"/> at once, and past that it is refused another.
    /// </summary>
    public static bool HasRoomFor(Subject subject, IEnumerable<PlaybackTicket> held)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(held);

        return held.Count(ticket => ticket.Subject.Equals(subject)) < MostHeldPerSubject;
    }
}
