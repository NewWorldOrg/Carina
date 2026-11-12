namespace Carina.Domain.Reservations;

/// <summary>
/// Why a reservation does not take the revision it was asked for.
/// </summary>
public enum ReservationRevisionRefusal
{
    AlreadyRecording = 1,

    NotCancelled = 2,

    AlreadyOver = 3,

    NotStanding = 4,
}
