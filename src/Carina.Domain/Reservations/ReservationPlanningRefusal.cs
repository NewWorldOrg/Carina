namespace Carina.Domain.Reservations;

public enum ReservationPlanningRefusal
{
    NoSuchProgramme = 1,

    Shadow = 2,

    MovedDuplicate = 3,

    AlreadyReserved = 4,
}
