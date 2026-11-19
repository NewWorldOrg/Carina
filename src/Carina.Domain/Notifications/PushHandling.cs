namespace Carina.Domain.Notifications;

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

    public static PushHandling UntilTheEnd(DateTime endsAt, DateTime at)
        => new(endsAt > at ? endsAt - at : TimeSpan.Zero, PushUrgency.High);
}
