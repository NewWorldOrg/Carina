namespace Carina.Domain.Reservations;

public sealed record ReservationOutcomeSettings
{
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMinutes(5);

    public TimeSpan Grace { get; init; } = DefaultGrace;
}
