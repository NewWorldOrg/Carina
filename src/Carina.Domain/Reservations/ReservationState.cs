namespace Carina.Domain.Reservations;

public enum ReservationState
{
    Scheduled = 1,

    Conflict = 2,

    Cancelled = 3,

    Missed = 4,
}
