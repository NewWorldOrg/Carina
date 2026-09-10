namespace Carina.Domain.Reservations;

/// <summary>
/// Why a reservation was taken out of the running. A person who cancels means it, and nothing else
/// puts it back; a reservation the guide stopped announcing was taken out on the guide's word, and
/// the guide is allowed to change its mind again.
/// </summary>
public enum ReservationCancellation
{
    ByHand = 1,

    ProgrammeGone = 2,
}
