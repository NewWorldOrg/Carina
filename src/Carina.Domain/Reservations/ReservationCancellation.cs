namespace Carina.Domain.Reservations;

/// <summary>
/// Why a reservation was taken out of the running: by a person, which nothing else undoes, or
/// because the guide stopped announcing it, which the guide may undo.
/// </summary>
public enum ReservationCancellation
{
    ByHand = 1,

    ProgrammeGone = 2,
}
