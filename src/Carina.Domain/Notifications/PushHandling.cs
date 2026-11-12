namespace Carina.Domain.Notifications;

/// <summary>
/// What a push service is asked to do with one message: how long to keep it for a device that is not reachable,
/// and how soon to wake the device for it.
/// </summary>
public sealed record PushHandling
{
    public static readonly TimeSpan UsualLifetime = TimeSpan.FromHours(24);

    public PushHandling(TimeSpan lifetime, PushUrgency urgency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lifetime, TimeSpan.Zero);

        if (!Enum.IsDefined(urgency))
        {
            throw new ArgumentOutOfRangeException(nameof(urgency), urgency, "A push message is of normal or of high urgency.");
        }

        Lifetime = lifetime;
        Urgency = urgency;
    }

    public static PushHandling Usual { get; } = new(UsualLifetime, PushUrgency.Normal);

    public TimeSpan Lifetime { get; }

    public PushUrgency Urgency { get; }

    /// <summary>
    /// The handling of a notice that a programme begins: urgent, and worth keeping only until the programme ends.
    /// </summary>
    public static PushHandling UntilTheEnd(DateTime endsAt, DateTime at)
        => new(endsAt > at ? endsAt - at : TimeSpan.Zero, PushUrgency.High);
}
