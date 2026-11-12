namespace Carina.Domain.Reservations;

/// <summary>
/// Why a programme asked for by hand is not reserved.
/// </summary>
public enum ReservationPlanningRefusal
{
    NoSuchProgramme = 1,

    Shadow = 2,

    MovedDuplicate = 3,

    AlreadyReserved = 4,
}
