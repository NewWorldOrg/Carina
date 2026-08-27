namespace Carina.Domain.Reservations;

public enum ReservationMove
{
    Keep = 1,

    Cancel = 2,

    Restore = 3,
}

public sealed record ReservationRevision
{
    public Priority? Priority { get; init; }

    public Margin? MarginBefore { get; init; }

    public Margin? MarginAfter { get; init; }

    public ReservationMove Move { get; init; } = ReservationMove.Keep;

    public bool ChangesNothing
        => Priority is null && MarginBefore is null && MarginAfter is null && Move is ReservationMove.Keep;
}
