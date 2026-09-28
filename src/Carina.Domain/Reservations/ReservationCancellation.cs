namespace Carina.Domain.Reservations;

/// <summary>
/// Why a reservation was taken out of the running: by a person, which nothing else undoes, because
/// the guide stopped announcing it, which the guide may undo, or because another reservation already
/// stands for the same moved broadcast, which a person may undo.
/// </summary>
public enum ReservationCancellation
{
    ByHand = 1,

    ProgrammeGone = 2,

    SameBroadcast = 3,
}
